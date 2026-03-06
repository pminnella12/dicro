using System;
using System.Collections.Generic;

namespace underdog
{
    public static class whereExtensions
    {
        public static IEnumerable<T> whereExt<T>(this IEnumerable<T> source, Func<bool, T> pred) {

            IEnumerable<T> values = new List<T>();


            return values;

        }
    }
}

