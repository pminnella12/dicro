// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*


Binary Search


*/

int[] array = { 2, 5, 6, 10, 11, 25, 33, 34, 41, 48, 49, 57, 66, 67, 80 };
int target = 34;
//expected 10

int outPut = SearchArray.BinarySearch(array, target);
Console.WriteLine(outPut.ToString());
outPut = SearchArray.BinarySearchRecursive(array, target, 0, array.Length - 1);
Console.WriteLine(outPut.ToString());

Console.Read();
public class SearchArray
{


	public static int BinarySearch(int[] array, int target)
	{
		int low = 0;
		int high = array.Length - 1;
		int mid;

		while (low <= high)
		{
			mid = low + ((high - low) / 2);
			if (array[mid] < target)
			{
				low = mid + 1;
			}
			else if (array[mid] > target)
			{
				high = mid - 1;
			}
			else
			{
				return mid;
			}
			
		}

		return -1; //not found

	}

	public static int BinarySearchRecursive(int[] array, int target, int low, int high)
	{
		if (low > high) return -1; //error

		int mid = low + ((high - low) / 2);

		if (array[mid] < target)
		{
			mid = BinarySearchRecursive(array, target, mid + 1, high);
		}
		else if (array[mid] > target)
		{
			mid = BinarySearchRecursive(array, target, low, mid - 1);
		}

		return mid;
		
	}


}