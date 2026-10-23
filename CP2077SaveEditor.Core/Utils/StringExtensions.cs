using System.Linq;

namespace CP2077SaveEditor.Extensions
{
    public static class StringExtensions
    {
        public static string LastOrIndex(this string[] arr, int position)
        {
            if (position == -1)
            {
                return arr.Last();
            }
            else
            {
                return arr[position];
            }
        }
    }
}
