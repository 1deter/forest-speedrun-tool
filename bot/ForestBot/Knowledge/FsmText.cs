using System.Text;

namespace ForestBot.Knowledge;

// ------------------------------------------------------------------
// A PlayMaker FSM export (docs/fsm/*.txt, src/Game/FsmExport.cs): a
// header (`fsm <name>  on <object>`, start state, [variables], [events],
// [global transitions]) then `[state <name>]` blocks. Pure, tested.
// ------------------------------------------------------------------
public sealed class FsmText
{
    /// The file's name without .txt ("mutant-action_combatFSM").
    public string Name;
    /// Everything before the first state: name, object, variables, events.
    public string Header = "";
    public List<(string state, string text)> States = new List<(string, string)>();

    public static FsmText Parse(string name, string text)
    {
        FsmText f = new FsmText { Name = name };
        string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        StringBuilder cur = new StringBuilder();
        string state = null;
        foreach (string line in lines)
        {
            if (line.StartsWith("[state ") && line.EndsWith("]"))
            {
                Flush(f, state, cur);
                state = line.Substring(7, line.Length - 8);
            }
            cur.Append(line).Append('\n');
        }
        Flush(f, state, cur);
        return f;
    }

    private static void Flush(FsmText f, string state, StringBuilder cur)
    {
        string t = cur.ToString().TrimEnd('\n');
        cur.Clear();
        if (state == null) f.Header = t;
        else f.States.Add((state, t));
    }

    /// The header plus the list of states with their transitions only - an
    /// overview the model can read before asking for one state.
    public string Overview()
    {
        StringBuilder b = new StringBuilder(Header).Append("\n\n[states: ").Append(States.Count).Append("]\n");
        foreach ((string state, string text) in States)
        {
            b.Append(state);
            List<string> on = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("on ")) on.Add(l.Substring(3));
            }
            if (on.Count > 0) b.Append(": ").Append(string.Join("; ", on));
            b.Append('\n');
        }
        return b.ToString();
    }

    /// One state's block, matched case-blind; null if there is none.
    public string State(string name)
    {
        foreach ((string state, string text) in States)
            if (string.Equals(state, name, StringComparison.OrdinalIgnoreCase)) return text;
        return null;
    }
}
