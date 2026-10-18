using System;

namespace ForestOverlay.Data
{
    /// A dropped copy's despawn timer (T-0418; game-notes *A dropped copy's
    /// despawn timer*): the game's `destroyAfter` checks once at `destroyTime`
    /// game seconds after the copy started, then every `Interval` seconds
    /// when it repeats (it has a distance). Unity cannot read the time left,
    /// so a restore resumes it from the copy's age at the capture. Pure.
    public static class DespawnTimer
    {
        /// The game's repeat interval (`InvokeRepeating("destroyMe", destroyTime, 10f)`).
        public const float Interval = 10f;

        /// Seconds from now to the next check of a copy `age` seconds old.
        /// Repeating: the first check before `destroyTime`, then the next
        /// 10 s step (a full step when exactly on one, as that check ran).
        /// Once: what is left of `destroyTime`, never below 0.
        public static float NextCheck(float age, float destroyTime, bool repeating)
        {
            if (age < 0f) age = 0f;
            if (!repeating) return Math.Max(0f, destroyTime - age);
            if (age < destroyTime) return destroyTime - age;
            return Interval - ((age - destroyTime) % Interval);
        }
    }
}
