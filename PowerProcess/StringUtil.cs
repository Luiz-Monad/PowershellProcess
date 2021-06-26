using System.Globalization;

namespace PowerProcess
{
    internal class StringUtil
    {
        internal static string Format(string format, object arg0)
            => string.Format(CultureInfo.CurrentCulture, format, arg0);

        internal static string Format(string format, object arg0, object arg1)
            => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);

        internal static string Format(string format, object arg0, object arg1, object arg2)
            => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1, arg2);

        internal static string Format(string format, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
