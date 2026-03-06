// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


int[] arr = { 3, 2, 4, 6, 5 };
int n = arr.Length;
if (GFG.checkTriplet2(arr, n))
    Console.Write("Yes");
else
    Console.Write("No");


public class GFG {

    public static bool checkTriplet(int[] arr, int n) {

        int maximum = 0;

        //find the maximum element
        for (int i = 0; i < n; i++) {

            maximum = Math.Max(maximum, arr[i]);
        }

        //hashing array
        int[] hash = new int[maximum + 1];


        //increase the count of array elements
        //in the hash table
        for (int i = 0; i < n; i++)
            hash[arr[i]]++;

        for (int i = 1; i < maximum + 1; i++) {

            //if a is not there
            if (hash[i] == 0)
                continue;

            for (int j = 1; j < maximum + 1; j++) {

                //if a and b are same and there is only one a
                //or if there is no b in original array
                if ((i == j && hash[i] == 1) || hash[j] == 0 )
                        continue;

                //find c
                int val = (int)Math.Sqrt(i * i + j * j);

                // if c^2 is not a perfect square
                if ((val * val) != (i * i + j * j))
                    continue;

                // if c exceeds the maximum value
                if (val > maximum)
                    continue;

                // if there exists c in the original array
                // we have the triplet

                if (hash[val] == 1) { return true; }
            }

        }
        return false;
    }

    public static bool checkTriplet2(int[] arr, int n)
    {

        // initializing unordered map with key and value as
        // integers
        Dictionary<int, int> umap = new Dictionary<int, int>();

        // Increase the count of array elements in unordered map
        for (int i = 0; i < n; i++)
            if (umap.ContainsKey(arr[i]))
                umap.Add(arr[i], umap[arr[i]] + 1);
            else
                umap.Add(arr[i], 1);

        for (int i = 0; i < n - 1; i++)
        {
            for (int j = i + 1; j < n; j++)
            {

                // calculating the squares of two elements as
                // integer and float
                int p = (int)Math.Sqrt(arr[i] * arr[i] + arr[j] * arr[j]);
                float q = (float)Math.Sqrt(arr[i] * arr[i] + arr[j] * arr[j]);

                // Condition is true if the value is same in
                // integer and float and also the value is
                // present in unordered map
                if (p == q && umap[p] != 0)
                    return true;
            }
        }

        // If we reach here, no triplet found
        return false;
    }

}