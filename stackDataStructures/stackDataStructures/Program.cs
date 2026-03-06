// See https://aka.ms/new-console-template for more information
using System.Text;

Console.WriteLine("Hello, World!");


int[] arry = { 34, 33, 12, 44, 2, 4, 6, 88, 15, 90, 3, 5 };
ArrayFunctions.GetSequentialNumbers(arry);

int[] arry1 = { 4, 5, 7, 88, 33, 12, 43 };
int[] arry2 = { 4, 42, 7, 88, 41, 12, 43 };

ArrayFunctions.GetUnion(arry1, arry2);
ArrayFunctions.GetIntersection(arry1, arry2);

public class ArrayFunctions {

    public static void GetSequentialNumbers(int[] arry)
    {
        HashSet<int> hashSet = new HashSet<int>();
        foreach (int value in arry)
        {
            hashSet.Add(value);
        }

        int highestCount = 0;
        string finalList = "";


        foreach (int value in arry) {
            StringBuilder sb = new StringBuilder();

            if (!hashSet.Contains(value - 1)) {
                int currentVal = value;
                int currentCount = 1;
                sb.Append(currentVal.ToString());
                sb.Append(" ");

                while (hashSet.Contains(currentVal+1)) {
                    
                    currentVal = currentVal + 1;
                    currentCount++;
                    sb.Append(currentVal.ToString());
                    sb.Append(" ");
                }

                if (currentCount > highestCount) {
                    highestCount = currentCount;
                    finalList = sb.ToString();
                }
            }
        }

        Console.WriteLine(highestCount.ToString());
        Console.WriteLine(finalList);
        
    }

    public static void GetUnion(int[] arry1, int[] arry2) {
        HashSet<int> hashSet = new HashSet<int>();
        StringBuilder sb = new StringBuilder();
        foreach (int value in arry1)
        {
            hashSet.Add(value);
            sb.Append(value.ToString());
            sb.Append(" ");
        }

        foreach (int value in arry2) {

            if (!hashSet.Contains(value)) {
                sb.Append(value.ToString());
                sb.Append(" ");
            }
        }

        Console.WriteLine(sb.ToString());

    }

    public static void GetIntersection(int[] arry1, int[] arry2)
    {
        HashSet<int> hashSet = new HashSet<int>();
        StringBuilder sb = new StringBuilder();
        foreach (int value in arry1)
        {
            hashSet.Add(value);
        }

        foreach (int value in arry2)
        {

            if (hashSet.Contains(value))
            {
                sb.Append(value.ToString());
                sb.Append(" ");
            }
        }

        Console.WriteLine(sb.ToString());

    }
}



