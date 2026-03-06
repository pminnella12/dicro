// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

char[] array = new char[] { 'a', 'a', 'a', 'a', '1', '1', 'a', '1', '1', 'a', 'a', '1', 'a', 'a', '1', 'a', 'a', 'a', 'a' };

var data = FindLongest.findLongestSubarray(array);
Console.WriteLine(data);
Console.Read();
public class FindLongest
{

    public static char[] findLongestSubarray(char[] array)
    {

        /* Compute deltas between count of numbers and count of letters */
        int[] deltas = computeDeltaArray(array);

        /* find pair of deltas with matching values and largest span */
        int[] match = findLongestMatch(deltas);

        /* Return the subarray. Note that it starts one *after* the initial occurence of this delta */

        return extract(array, match[0] + 1, match[1]);

    }

    // compute the difference between the number of letters and numbers between the
    //beginning of the array and each index
    public static int[] computeDeltaArray(char[] array)
    {

        int[] deltas = new int[array.Length];
        int delta = 0;
        int x;
        for (int i = 0; i < array.Length; i++)
        {
            if (Char.IsNumber(array[i]))
            {
                delta--;
            }
            else if (Char.IsLetter(array[i]))
            {

                delta++;
            }

            deltas[i] = delta;
        }

        return deltas;
    }

    // find the matching pair of values in the deltas array with the largest
    // difference in indices
    public static int[] findLongestMatch(int[] deltas) {
        Dictionary<int, int> map = new Dictionary<int, int>();

        map.Add(0, -1);
        int[] max = new int[2];

        for (int i = 0; i < deltas.Length; i++) {
            if (!map.ContainsKey(deltas[i]))
            {
                map.Add(deltas[i], i);
            }
            else {

                int match = map[deltas[i]];
                int distance = i - match;
                int longest = max[1] - max[0];
                if (distance > longest) {
                    max[1] = i;
                    max[0] = match;

                }
            }
        }
        return max;
    }

    public static char[] extract(char[] array, int start, int end) {
        char[] data = new char[100];
        while (start < end) {
            data[start] = array[start];
            start++;
        }

        return data; 
    }
}