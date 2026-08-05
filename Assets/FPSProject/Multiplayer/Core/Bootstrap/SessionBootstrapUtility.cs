using System;
using System.Text;

namespace FPSProject.Multiplayer.Core.Bootstrap
{
    public static class SessionBootstrapUtility
    {
        public static string NormalizeJoinCode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        public static string BuildAuthenticationProfile(string requested, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(requested) ? fallback : requested;
            if (string.IsNullOrWhiteSpace(source)) source = "fps-player";

            var result = new StringBuilder(30);
            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') result.Append(c);
                if (result.Length == 30) break;
            }

            return result.Length > 0 ? result.ToString() : "fps-player";
        }

        public static bool HasCommandLineFlag(string[] args, string flag)
        {
            if (args == null || string.IsNullOrWhiteSpace(flag)) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string GetCommandLineValue(string[] args, string key)
        {
            if (args == null || string.IsNullOrWhiteSpace(key)) return string.Empty;
            string prefix = key + "=";
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg != null && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length);
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                    return args[i + 1] ?? string.Empty;
            }
            return string.Empty;
        }

        public static int GetPositiveCommandLineInt(string[] args, string key, int fallback)
        {
            string value = GetCommandLineValue(args, key);
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }
    }
}
