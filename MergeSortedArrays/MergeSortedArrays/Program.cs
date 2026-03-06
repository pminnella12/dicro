// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");



/* Sorted Merge
You are given two sorted arrays, A and B, where A has a large enough buffer at the
end to hold B. Write a method to merge B into A in sorted order. 

HINT - Try moving from the end ofthe array to the beginning.

A
[3, 5, 7, 8, 10, [0], [0], [0], [0], [0]]

B
[2, 4, 9, 11, 12]

//now merge sorted arrays


//shift A down
[X, X, X, X, X, 3, 5, 7, 8, 10]
[2, X, X, X, X, 3, 5, 7, 8, 10]
[2, 3, X, X, X, 3, 5, 7, 8, 10]
[2, 3, 4, X, X, 3, 5, 7, 8, 10]
[2, 3, 4, 5, X, 3, 5, 7, 8, 10]
[2, 3, 4, 5, 7, 3, 5, 7, 8, 10]
[2, 3, 4, 5, 7, 8, 5, 7, 8, 10]
[2, 3, 4, 5, 7, 8, 9, 7, 8, 10]
[2, 3, 4, 5, 7, 8, 9, 10, 8, 10]
[2, 3, 4, 5, 7, 8, 9, 10, 11, 10]
[2, 3, 4, 5, 7, 8, 9, 10, 11, 12]

*/

int[] arrayA = { 3, 5, 10, 11, 12, 0, 0, 0, 0, 0 };
int[] arrayB = { 2, 4, 7, 8, 9 };

Merger.MergeSortedArray(arrayA, arrayB);


public class Merger
{


	public static void MergeSortedArray(int[] arrayA, int[] arrayB)
	{

		Console.WriteLine("Array A: ");
		PrintArray(arrayA);
		Console.WriteLine("Array Big: ");
		PrintArray(arrayB);

		//shift values to end of big array
		var shift = arrayB.Length;

		for (int i = arrayA.Length - 1; i >= 0; i--)
		{
			if (arrayA[i] != 0)
			{
				arrayA[i + shift] = arrayA[i];
				arrayA[i] = 0;
			}
		}
		Console.WriteLine("Array A: ");
		PrintArray(arrayA);

		MergeArray(arrayA, arrayB);
	}


	public static void PrintArray(int[] array)
	{

		string output = "[ ";

		for (int i = 0; i < array.Length; i++)
		{
			output += array[i].ToString() + ",";
		}

		output = output.TrimEnd(',');
		output += " ]";
		Console.WriteLine(output);
	}

	/*
		A
		[0, 0, 0, 0, 0, 3, 5, 7, 8, 10]

		B
		[2, 4, 9, 11, 12]

		indexA-5
		indexB-0
		indexCurrent-0

		3 > 2
		[2, 0, 0, 0, 0, 3, 5, 7, 8, 10]
		indexA-5 
		indexB-1
		indexCurrent-1

		1 > 5 and 12 < 3 -false

		1 < 10 true
		3 > 4 false
		[2, 3, 0, 0, 0, 3, 5, 7, 8, 10]
		indexA-6 
		indexB-1
		indexCurrent-2

		1 > 5 and 12 < 3 -false
		false

		[2, 3, 4, 5, 7, 8, 9, 10, [8], (10)]
		[2, 4, 9, (11), 12]
		indexA-9 
		indexB-3
		indexCurrent-7

	*/
	public static void MergeArray(int[] arrayA, int[] arrayB)
	{

		int indexA = arrayB.Length;
		int indexB = 0;
		int indexCurrent = 0;

		while (indexCurrent < arrayA.Length)
		{

			if (indexA == arrayA.Length)
			{
				arrayA[indexCurrent] = arrayB[indexB];
				indexB++;
				indexCurrent++;
				PrintArray(arrayA);
				continue;
			}

			if (arrayA[indexA] > arrayB[indexB])
			{
				arrayA[indexCurrent] = arrayB[indexB];
				indexB++;
			}
			else
			{
				arrayA[indexCurrent] = arrayA[indexA];
				indexA++;
			}

			indexCurrent++;
			PrintArray(arrayA);

			if (indexB == arrayB.Length &&
				arrayB[arrayB.Length - 1] < arrayA[indexA])
			{
				break;
			}
		}

	}
}


