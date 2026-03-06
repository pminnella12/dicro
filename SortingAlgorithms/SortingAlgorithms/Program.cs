// See https://aka.ms/new-console-template for more information
using System.Text;

Console.WriteLine("Hello, World!");





/*
int[] array = new int[] { 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };
SortingAlgorithms.PrintArray(array);
SortingAlgorithms.InsersionSort(array);
SortingAlgorithms.PrintArray(array);
array = new int[] { 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };
SortingAlgorithms.PrintArray(array);
SortingAlgorithms.SelectionSort(array);
SortingAlgorithms.PrintArray(array);
array = new int[] { 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };
SortingAlgorithms.PrintArray(array);
SortingAlgorithms.BubbleSort(array);
SortingAlgorithms.PrintArray(array);
int[] array = new int[] { 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };
SortingAlgorithms.PrintArray(array);
SortingAlgorithms.MergeSort(array);
SortingAlgorithms.PrintArray(array);*/

int[] array = new int[] { 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };
SortingAlgorithms.PrintArray(array);
SortingAlgorithms.QuickSort(array, 0, array.Length-1);
SortingAlgorithms.PrintArray(array);


public static class SortingAlgorithms
{


	/*

	Insertion Sort

	*/

	//Insertion sort is a simple sorting algorithm that works similar to the way you sort playing cards in your hands.
	//start at begining, compare first 2 then swap is second is smaller.
	//move to next node and repeat. If swapped node is still smaller than prev swap again
	//repeat until done
	public static void InsersionSort(int[] array)
	{
		int currentIndex = 1;
		while (currentIndex < array.Length)
		{
			int prevIndex = currentIndex - 1;
			CompareAndSwapInsersionSort(array, prevIndex, currentIndex);
			currentIndex++;
		}
	}

	private static void CompareAndSwapInsersionSort(int[] array, int prevIndex, int currentIndex)
	{
		if (currentIndex < array.Length && prevIndex >= 0)
		{
			if (array[currentIndex] < array[prevIndex])
			{
				Swap(array, prevIndex, currentIndex);
				prevIndex--;
				currentIndex--;
				CompareAndSwapInsersionSort(array, prevIndex, currentIndex);
			}
		}
	}

	/*

	Selection Sort

	*/

	//The selection sort algorithm sorts an array by repeatedly finding the minimum element (considering ascending order)
	//from unsorted part and putting it at the beginning.

	public static void SelectionSort(int[] array) {
		int currentIndex = 0;

		while (currentIndex < array.Length) {
			int minValue = int.MaxValue;
			int minValueIndex = int.MaxValue;
			for (int i = currentIndex; i < array.Length; i++) {
				if (array[i] < minValue) {
					minValue = array[i];
					minValueIndex = i;
				}
			}

			if (array[currentIndex] > minValue) {
				Swap(array, currentIndex, minValueIndex);
			}

			currentIndex++;
		}
	}

	/*

	Bubble Sort

	Bubble Sort is the simplest sorting algorithm that works by repeatedly swapping the adjacent elements if they are in the wrong order.
	This algorithm is not suitable for large data sets as its average and worst-case time complexity is quite high.
	This will be done if it goes through whole list without any swaps

	*/

	public static void BubbleSort(int[] array) {

		bool swapMade = true;

		while (swapMade) {
			swapMade = false;

			for (int i = 0; i < array.Length-1; i++) {
				if (array[i] > array[i + 1]) {
					Swap(array, i, i + 1);
					swapMade = true;
				}
			}

		}

	}

	/*
	 MERGE SORT

	So, in this algorithm, the array is initially divided into two equal halves and then they are combined in a sorted manner. We can think of it as a
	recursive algorithm that continuously splits the array in half until it cannot be further divided. This means that if the array becomes empty or
	has only one element left, the dividing will stop, i.e. it is the base case to stop the recursion. If the array has multiple elements, we split
	the array into halves and recursively invoke the merge sort on each of the halves. Finally, when both the halves are sorted, the merge operation
	is applied. Merge operation is the process of taking two smaller sorted arrays and combining them to eventually make a larger one.
	 */

	public static void MergeSort(int[] array) {

		sort(array, 0, array.Length-1);
	}

	public static void merge(int[] arr, int left, int middle, int right)
	{
		// Find sizes of two
		// subarrays to be merged
		int lSize = middle - left + 1;
		int rSize = right - middle;

		// Create temp arrays
		int[] lArray = new int[lSize];
		int[] rArray = new int[rSize];
		int i, j;

		// Copy data to temp arrays
		for (i = 0; i < lSize; ++i)
			lArray[i] = arr[left + i];
		for (j = 0; j < rSize; ++j)
			rArray[j] = arr[middle + 1 + j];

		// Merge the temp arrays

		// Initial indexes of first
		// and second subarrays
		i = 0;
		j = 0;

		// Initial index of merged
		// subarray array
		int k = left;
		while (i < lSize && j < rSize)
		{
			if (lArray[i] <= rArray[j])
			{
				arr[k] = lArray[i];
				i++;
			}
			else
			{
				arr[k] = rArray[j];
				j++;
			}
			k++;
		}

		// Copy remaining elements
		// of L[] if any
		while (i < lSize)
		{
			arr[k] = lArray[i];
			i++;
			k++;
		}

		// Copy remaining elements
		// of R[] if any
		while (j < rSize)
		{
			arr[k] = rArray[j];
			j++;
			k++;
		}
	}

	// Main function that
	// sorts arr[l..r] using
	// merge()
	public static void sort(int[] arr, int left, int right)
	{
		if (left < right)
		{
			// Find the middle
			// point
			int middle = left + (right-left) / 2;

			// Sort first and
			// second halves
			sort(arr, left, middle);
			sort(arr, middle + 1, right);

			// Merge the sorted halves
			merge(arr, left, middle, right);
		}
	}


	/*
	 * QUICK SORT
	 * 
	 */

	//It picks an element as a pivot and partitions the given array around the picked pivot.There are many different versions of quickSort that pick pivot in different ways.

	//Always pick the first element as a pivot.
	//Always pick the last element as a pivot (implemented below)
	//Pick a random element as a pivot.
	//Pick median as the pivot.
	//The key process in quickSort is a partition(). The target of partitions is, given an array and an element x of an array as the pivot, put x at its correct
	//position in a sorted array and put all smaller elements (smaller than x) before x, and put all greater elements(greater than x) after x.All this should be done in linear time.

	/* The main function that implements QuickSort
				arr[] --> Array to be sorted,
				low --> Starting index,
				high --> Ending index
	{ 5, 33, 4, 7, 23, 43, 20, 3, 33, 9, 14, 2, 66 };


	   */
	public static void QuickSort(int[] arr, int low, int high)
	{
		if (low < high)
		{

			// pi is partitioning index, arr[p]
			// is now at right place 
			int pi = Partition(arr, low, high);

			// Separately sort elements before
			// partition and after partition
			QuickSort(arr, low, pi - 1);
			QuickSort(arr, pi + 1, high);
		}
	}



	/* This function takes last element as pivot, places
       the pivot element at its correct position in sorted
       array, and places all smaller (smaller than pivot)
       to left of pivot and all greater elements to right
       of pivot */
	public static int Partition(int[] arr, int low, int high)
	{

		// pivot
		int pivot = arr[high];

		// Index of smaller element and
		// indicates the right position
		// of pivot found so far
		int i = (low - 1);

		/*


		2, 4*, 33, 7, 23, 43, 20, 3, 33, 9, 14, (5), 66 

		 

		*/
		for (int j = low; j <= high - 1; j++)
		{

			// If current element is smaller 
			// than the pivot
			if (arr[j] < pivot)
			{

				// Increment index of 
				// smaller element
				i++;
				Swap(arr, i, j);
			}
		}
		Swap(arr, i + 1, high);
		return (i + 1);
	}

	
	/*
	 * UTILITY FUNCTIONS 
	 */

	private static void Swap(int[] array, int index1, int index2)
	{

		var temp = array[index1];
		array[index1] = array[index2];
		array[index2] = temp;

	}

	public static void PrintArray(int[] array)
	{

		StringBuilder sb = new StringBuilder();

		for (int i = 0; i < array.Length; i++)
		{
			sb.Append("[");
			sb.Append(array[i].ToString());
			sb.Append("] ");
		}
		Console.WriteLine(sb.ToString());

	}
}