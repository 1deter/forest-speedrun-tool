using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Game/SceneCache's rules (T-0148): kept while usable, searched again
    // when an entry is gone / switched off, nothing found never kept,
    // everything forgotten on Clear (a scene loaded or unloaded).
    // ------------------------------------------------------------------
    public class LookupCacheTests
    {
        private sealed class Obj
        {
            public bool Alive = true;
            public bool Active = true;
        }

        private static bool Usable(Obj o) { return o != null && o.Alive && o.Active; }

        private sealed class Scene
        {
            public readonly List<Obj> Objects = new List<Obj>();
            public int Searches;

            public Obj One(string key)
            {
                Searches++;
                foreach (Obj o in Objects) if (Usable(o)) return o;
                return null;
            }

            public Obj[] All(string key)
            {
                Searches++;
                List<Obj> l = new List<Obj>();
                foreach (Obj o in Objects) if (Usable(o)) l.Add(o);
                return l.ToArray();
            }
        }

        [Fact]
        public void OneIsKeptWhileUsable()
        {
            Scene s = new Scene();
            Obj a = new Obj();
            s.Objects.Add(a);
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            Assert.Same(a, c.One("t", s.One));
            Assert.Same(a, c.One("t", s.One));
            Assert.Equal(1, s.Searches);
            Assert.Equal(1, c.Hits);
        }

        [Fact]
        public void OneSearchesAgainWhenDestroyedOrInactive()
        {
            Scene s = new Scene();
            Obj a = new Obj(), b = new Obj();
            s.Objects.Add(a);
            s.Objects.Add(b);
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            Assert.Same(a, c.One("t", s.One));
            a.Alive = false;
            Assert.Same(b, c.One("t", s.One));
            b.Active = false;
            Assert.Null(c.One("t", s.One));
            Assert.Equal(3, s.Searches);
        }

        [Fact]
        public void NothingFoundIsNotKept()
        {
            Scene s = new Scene();
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            Assert.Null(c.One("t", s.One));
            Assert.Empty(c.All("u", s.All));
            Obj a = new Obj();
            s.Objects.Add(a);   // appears later (a scene object made / enabled)
            Assert.Same(a, c.One("t", s.One));
            Assert.Single(c.All("u", s.All));
            Assert.Equal(4, s.Searches);
        }

        [Fact]
        public void AllIsKeptWhileEveryEntryIsUsable()
        {
            Scene s = new Scene();
            s.Objects.Add(new Obj());
            s.Objects.Add(new Obj());
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            Obj[] first = c.All("t", s.All);
            Assert.Same(first, c.All("t", s.All));
            Assert.Equal(1, s.Searches);
        }

        [Fact]
        public void AllSearchesAgainWhenAnEntryIsGoneOrInactive()
        {
            Scene s = new Scene();
            Obj a = new Obj(), b = new Obj();
            s.Objects.Add(a);
            s.Objects.Add(b);
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            Assert.Equal(2, c.All("t", s.All).Length);
            b.Active = false;
            Assert.Single(c.All("t", s.All));
            a.Alive = false;
            Assert.Empty(c.All("t", s.All));
            Assert.Equal(3, s.Searches);
        }

        [Fact]
        public void ClearForgetsEverything()
        {
            Scene s = new Scene();
            s.Objects.Add(new Obj());
            LookupCache<string, Obj> c = new LookupCache<string, Obj>(Usable);
            c.One("t", s.One);
            c.All("u", s.All);
            c.Clear();
            c.One("t", s.One);
            c.All("u", s.All);
            Assert.Equal(4, s.Searches);
            Assert.Equal(0, c.Hits);
        }
    }
}
