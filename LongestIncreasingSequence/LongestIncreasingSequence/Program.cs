// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


int[] arr = { 10, 22, 9, 33, 21, 50, 41, 60 };
int n = arr.Length;
Console.Write("Length of lis is " + LongestIncreasingSequence.Recursion(arr, n)
              + "\n");


Console.WriteLine("Length of lis is " + LongestIncreasingSequence.DynamicLIS(arr, n)
                  + "\n");
Console.Read();

public class LongestIncreasingSequence {

    public static int DynamicLIS(int[] array, int length) {
        int[] lis = new int[length];
        int i, j, max = 0;

        /* Initialize LIS values for all indexes */
        for (i = 0; i < length; i++)
            lis[i] = 1;

        /* Compute optimized LIS values in bottom up manner
         */
        for (i = 1; i < length; i++)
            for (j = 0; j < i; j++)
                if (array[i] > array[j] && lis[i] < lis[j] + 1)
                    lis[i] = lis[j] + 1;

        /* Pick maximum of all LIS values */
        for (i = 0; i < length; i++)
            if (max < lis[i])
                max = lis[i];

        return max;
    }


    /// <summary>
    /// RECURSION
    /// </summary>
    public static int maxRef;

    public static int _Recursion(int[] arry, int arryLength) {

        // base case
        if (arryLength == 1) { return 1; }

        //max ending here  is the length  of LIS ending with arry[n-1]
        int result, maxEndingHere = 1;


        /* Recursively get all LIS ending with arry[0],
           arry[1] ... arry[arryLength-2]. If   arry[i-1] is smaller
           than arry[arryLength-1], and max ending with arry[arrayLength-1] needs
           to be updated, then update it */
        for (int i = 1; i < arryLength; i++)
        {
            result = Recursion(arry, i);
            if (arry[i - 1] < arry[arryLength - 1]
                && result + 1 > maxEndingHere)
                maxEndingHere = result + 1;
        }

        // Compare max_ending_here with the overall max. And
        // update the overall max if needed
        if (maxRef < maxEndingHere)
            maxRef = maxEndingHere;

        // Return length of LIS ending with arr[n-1]
        return maxEndingHere;
    }

    // The wrapper function for _lis()
    public static int Recursion(int[] arr, int n)
    {
        // The max variable holds the result
        maxRef = 1;

        // The function _lis() stores its result in max
        _Recursion(arr, n);

        // returns max
        return maxRef;
    }

}


