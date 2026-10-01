using System.Collections.Generic;
using System.Diagnostics;

namespace LockerColorMod
{
    // Times a piece of code: `using (new Probe("name")) { ... }`. Free when it's fast; when it takes
    // longer than 8 ms the log says which piece and how long (at most one line per piece every 5 s,
    // and 60 in all), so a frame hitch can be traced to the code behind it.
    internal readonly struct Probe : System.IDisposable
    {
        private readonly string _name;
        private readonly long _start;
        private static readonly Dictionary<string, long> _lastLogged = new Dictionary<string, long>();
        private static int _logged;

        public Probe(string name) { _name = name; _start = Stopwatch.GetTimestamp(); }

        public void Dispose()
        {
            long now = Stopwatch.GetTimestamp();
            double ms = (now - _start) * 1000.0 / Stopwatch.Frequency;
            if (ms < 8.0 || _logged >= 60) return;
            if (_lastLogged.TryGetValue(_name, out long last) && (now - last) < 5 * Stopwatch.Frequency) return;
            _lastLogged[_name] = now;
            _logged++;
            MelonLoader.MelonLogger.Msg($"[Cost] {_name} took {ms:0} ms.");
        }
    }
}
