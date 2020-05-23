using System;

namespace VideoBackupper
{
    static class Utils
    {
        private static object _lock = new object();
        private static int _lastLength = 0;

        public static void WriteLine(string value)
        {
            lock (_lock)
            {
                Console.WriteLine(value.PadRight(_lastLength));
                _lastLength = 0;
            }
        }

        public static void Write(string value)
        {
            lock (_lock)
            {
                Console.Write(value.PadRight(_lastLength));
                _lastLength = Console.CursorLeft;
                Console.CursorLeft = 0;
            }
        }
    }
}
