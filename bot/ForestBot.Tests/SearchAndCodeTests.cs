using ForestBot.Code;
using ForestBot.Knowledge;
using ForestBot.Search;
using Xunit;

namespace ForestBot.Tests;

public class SearchAndCodeTests
{
    [Fact]
    public void Fts_query_drops_stop_words_and_quotes_terms()
    {
        Assert.Equal("\"bomb\" OR \"boost\" OR \"fps\"", KeywordIndex.ToMatch("Why does a bomb boost work at high-ish fps?").Replace(" OR \"high\" OR \"ish\"", ""));
        Assert.Equal("\"HandleLanded\"", KeywordIndex.ToMatch("HandleLanded"));
        Assert.Equal("\"playerHitReactions\" OR \"enableExplodeCamera\"", KeywordIndex.ToMatch("playerHitReactions.enableExplodeCamera"));
        Assert.Equal("", KeywordIndex.ToMatch("how does it work?"));
        Assert.Equal("\"210\"", KeywordIndex.ToMatch("keycard 210").Replace("\"keycard\" OR ", ""));
        // nothing a runner types can break the FTS syntax
        Assert.DoesNotContain("NEAR(", KeywordIndex.ToMatch("NEAR( \" * ^ :col"));
    }

    [Fact]
    public void Keyword_search_ranks_titles_and_aliases_first()
    {
        Corpus c = new Corpus();
        c.AddCard(Card.Parse("---\nid: bomb-boost\ntitle: Bomb boost\naliases: bb, pause boost\nconfidence: live\n---\n# Bomb boost\n\nExplosion pushes.\n\n## Numbers\n\n8 m/s per frame.\n", "bomb-boost"));
        c.AddCard(Card.Parse("---\nid: fall-damage\ntitle: Fall damage\naliases: slide cancel\nconfidence: code\n---\n# Fall damage\n\nLanding over 28 m/s hurts. A boost can end in a fall.\n", "fall-damage"));
        using HybridSearch s = new HybridSearch(c, null, null, null);
        Assert.StartsWith("card:bomb-boost", s.Search("bb", 5)[0].Chunk.Id);
        Assert.StartsWith("card:fall-damage", s.Search("slide cancel", 5)[0].Chunk.Id);
        Assert.StartsWith("card:bomb-boost", s.Search("boost", 5)[0].Chunk.Id);
        Assert.Empty(s.Search("the", 5));
        Assert.All(s.Search("boost", 5, new[] { "doc" }), h => Assert.Equal("doc", h.Chunk.Kind));
    }

    private sealed class FakeEmbedder : IEmbedder
    {
        public string ModelId => "fake";
        // A 2-d "meaning": does the text talk about flying or about landing?
        private static float[] V(string t) => Vectors.Normalize(new[]
        {
            t.Contains("fly") || t.Contains("explosion") ? 1f : 0.01f,
            t.Contains("land") || t.Contains("ground") ? 1f : 0.01f,
        });
        public float[] EmbedDocument(string text) => V(text.ToLowerInvariant());
        public float[] EmbedQuery(string text) => V(text.ToLowerInvariant());
    }

    [Fact]
    public void Vectors_find_paraphrases_keywords_miss_and_are_cached()
    {
        Corpus c = new Corpus();
        c.AddCard(Card.Parse("---\nid: bomb-boost\ntitle: Bomb boost\naliases: bb\nconfidence: live\n---\n# Bomb boost\n\nAn explosion knockback stacked in the pause menu.\n", "bomb-boost"));
        c.AddCard(Card.Parse("---\nid: fall-damage\ntitle: Fall damage\naliases: slide cancel\nconfidence: code\n---\n# Fall damage\n\nHitting the ground fast hurts.\n", "fall-damage"));
        string cache = Path.Combine(Path.GetTempPath(), "fb-vec-" + Guid.NewGuid().ToString("N") + ".db");
        List<string> log = new List<string>();
        using (HybridSearch s = new HybridSearch(c, new FakeEmbedder(), cache, log.Add))
        {
            // no shared words with either card - only the "meaning" matches
            List<Hit> hits = s.Search("why do I fly so far", 5);
            Assert.Equal("card:bomb-boost", hits[0].Chunk.Id);
            Assert.Equal(-1, hits[0].KeywordRank);
        }
        using (new HybridSearch(c, new FakeEmbedder(), cache, log.Add)) { }
        Assert.Contains(log, l => l.Contains("2 embedded now"));
        Assert.Contains(log, l => l.Contains("0 embedded now, 2 from the cache"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(cache);
    }

    private const string File1 = "using UnityEngine;\n\nnamespace TheForest.World;\n\npublic class ElevatorSystem : MonoBehaviour\n{\n" +
        "\t[Serializable]\n\tpublic class MessageToGo\n\t{\n\t\tpublic GameObject _target;\n\n\t\tpublic string _message;\n\t}\n\n" +
        "\tpublic int _useLimit;\n\n\tprivate int _useCount = 0;\n\n" +
        "\tpublic void GotoRemotePoint()\n\t{\n\t\tif (_useLimit <= 0 || _useCount < _useLimit)\n\t\t{\n\t\t\tStartCoroutine(Goto());\n\t\t}\n\t}\n\n" +
        "\tprivate IEnumerator Goto()\n\t{\n\t\tLocalPlayer.SpecialActions.SendMessage(\"openDoorRoutine\", _playerPos);\n\t\tyield return null;\n\t}\n\n" +
        "\tpublic bool Moving => _useCount > 0;\n\n\tpublic float Speed\n\t{\n\t\tget\n\t\t{\n\t\t\treturn 1f;\n\t\t}\n\t}\n}\n";

    private const string File2 = "public class playerOpenKeypadDoorAction : MonoBehaviour\n{\n" +
        "\tpublic void openKeypadDoor(Transform pos)\n\t{\n\t\tStartCoroutine(openDoorRoutine(pos));\n\t}\n\n" +
        "\tpublic IEnumerator openDoorRoutine(Transform pos)\n\t{\n\t\tyield return null;\n\t}\n\n" +
        "\tpublic void openDoorRoutine(int overload)\n\t{\n\t}\n}\n";

    private static CodeIndex Index()
    {
        CodeIndex idx = new CodeIndex();
        idx.AddFile("TheForest.World/ElevatorSystem.cs", File1);
        idx.AddFile("playerOpenKeypadDoorAction.cs", File2);
        return idx;
    }

    [Fact]
    public void Code_index_reads_members_types_and_nested_types()
    {
        CodeIndex idx = Index();
        Assert.Equal(3, idx.Types.Count);
        CodeIndex.TypeEntry t = idx.FindTypes("ElevatorSystem")[0];
        Assert.Equal("TheForest.World.ElevatorSystem", t.FullName);
        Assert.Equal(new[] { "_useLimit", "_useCount", "GotoRemotePoint", "Goto", "Moving", "Speed" }, t.Members.Select(m => m.Name));
        Assert.Single(idx.FindTypes("ElevatorSystem.MessageToGo"));
        Assert.Single(idx.FindTypes("TheForest.World.ElevatorSystem"));

        string body = idx.Read("ElevatorSystem.GotoRemotePoint");
        Assert.Contains("ElevatorSystem.cs:19", body);
        Assert.Contains("if (_useLimit <= 0 || _useCount < _useLimit)", body);
        Assert.EndsWith("}\n", body);
        Assert.DoesNotContain("Goto()\n{", body.Replace("StartCoroutine(Goto());", ""));

        string overloads = idx.Read("playerOpenKeypadDoorAction::openDoorRoutine()");
        Assert.Contains("IEnumerator openDoorRoutine(Transform pos)", overloads);
        Assert.Contains("openDoorRoutine(int overload)", overloads);

        string outline = idx.Read("ElevatorSystem");
        Assert.Contains("private int _useCount = 0;", outline);
        Assert.Contains("nested: public class MessageToGo", outline);
        Assert.Contains("No member 'Nope'", idx.Read("ElevatorSystem.Nope"));
        Assert.Contains("Nothing called", idx.Read("Missing.Thing"));
    }

    [Fact]
    public void Code_search_finds_names_and_string_calls()
    {
        string r = Index().Search("openDoorRoutine");
        Assert.Contains("playerOpenKeypadDoorAction.openDoorRoutine", r);
        Assert.Contains("ElevatorSystem.Goto (TheForest.World/ElevatorSystem.cs:", r);   // the SendMessage caller
        Assert.Contains("playerOpenKeypadDoorAction.openKeypadDoor", r);
        Assert.Contains("not available", new CodeIndex().Search("x y"));
    }
}
