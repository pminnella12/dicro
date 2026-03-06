// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


/*
Write an efficient program for printing k largest elements in an array. Elements in an array can be in any order.
For example: if the given array is [1, 23, 12, 9, 30, 2, 50] and you are asked for the largest 3 elements i.e., k = 3 then your program should print 50, 30, and 23.
*/


int[] array = { 1, 23, 12, 9, 30, 2, 50 };

//KLargest.findTopLargestWithBubbleSort(array, 3);
//KLargest.findTopLargestWithSelectionSort(array, 3);
KLargest.tempArray(array, 3);
public class KLargest
{

	public static void maxHeap(int[] array, int count) {

	}

	public static void tempArray(int[] array, int count)
	{


		int tempArrayCount = count;
		if (array.Length == 0) return;
		if (array.Length < count) { tempArrayCount = array.Length - 1; }

		int[] tempArray = new int[tempArrayCount];


		//copy k values into tempArray
		for (int i = 0; i < tempArrayCount; i++)
		{
			tempArray[i] = array[i];

		}

		//update temp array with larger values
		for (int j = tempArrayCount - 1; j < array.Length; j++)
		{
			var minValueIndex = getMinValueFromArray(tempArray);
			if (tempArray[minValueIndex] < array[j])
			{
				tempArray[minValueIndex] = array[j];
			}
		}


		for (int k = 0; k < tempArray.Length; k++)
		{
			Console.WriteLine("K value: " + tempArray[k].ToString());
		}

	}

	private static int getMinValueFromArray(int[] array)
	{

		var smallestValue = int.MaxValue;
		var minValueIndex = int.MaxValue;
		for (int i = 0; i < array.Length; i++)
		{
			if (smallestValue > array[i])
			{
				smallestValue = array[i];
				minValueIndex = i;
			}
		}

		return minValueIndex;
	}

	//O(n*k)
	public static void findTopLargestWithBubbleSort(int[] array, int count)
	{


		var continueScan = true;
		//bubble sort the array
		while (count > 0)
		{
			//continue scan is used to sort the whole array
			count--;
			continueScan = scanBubbleSortArray(array);
		}

		var max1 = array[array.Length - 1];
		var max2 = array[array.Length - 2];
		var max3 = array[array.Length - 3];

		Console.WriteLine(max1.ToString() + "," + max2.ToString() + "," + max3.ToString());
		Console.WriteLine(array[0].ToString() + "," + array[1].ToString() + "," + array[2].ToString() + "," + array[3].ToString() + "," + array[4].ToString() + "," + array[5].ToString() + "," + array[6].ToString());

	}

	//O(n*k)
	public static void findTopLargestWithSelectionSort(int[] array, int count)
	{

		if (array.Length < count) return;
		scanSelectionSortArray(array);

		var max1 = array[0];
		var max2 = array[1];
		var max3 = array[2];

		Console.WriteLine(max1.ToString() + "," + max2.ToString() + "," + max3.ToString());
		Console.WriteLine(array[0].ToString() + "," + array[1].ToString() + "," + array[2].ToString() + "," + array[3].ToString() + "," + array[4].ToString() + "," + array[5].ToString() + "," + array[6].ToString());

	}

	private static void scanSelectionSortArray(int[] array)
	{


		for (int scanIndex = 0; scanIndex < 3/*array.Length - 1*/; scanIndex++)
		{

			var maxValue = int.MinValue;
			var maxValueIndex = 0;
			for (int searchIndex = scanIndex + 1; searchIndex < array.Length; searchIndex++)
			{
				if (array[searchIndex] > maxValue && array[scanIndex] < array[searchIndex])
				{

					maxValue = array[searchIndex];
					maxValueIndex = searchIndex;
					Console.WriteLine("maxValue:" + maxValue + ", " + maxValueIndex);
				}
			}


			if (maxValueIndex > scanIndex && array[scanIndex] < array[maxValueIndex])
			{
				swapIndexes(array, scanIndex, maxValueIndex);
			}
		}
	}


	private static bool scanBubbleSortArray(int[] array)
	{
		int i = 0;
		int j = 1;
		bool swapMade = false;
		if (array == null || array.Length < 2) return false;

		for (i = 0; i < array.Length - 1 && j < array.Length; i++)
		{

			if (array[i] > array[j])
			{
				swapIndexes(array, i, j);
				swapMade = true;
			}
			j++;
		}

		return swapMade;
	}

	private static void swapIndexes(int[] array, int index1, int index2)
	{
		var val1 = array[index1];
		var val2 = array[index2];

		array[index1] = val2;
		array[index2] = val1;

		Console.WriteLine("swapping: " + val1.ToString() + ",  " + val2.ToString());

	}

}