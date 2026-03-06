// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/* find missing number

You are given an array of positive numbers from 1 to n, such that all numbers from 1 to n are present except one number x. 
You have to find x. The input array is not sorted. Look at the below array and give it a try before checking the solution.

[ 3, 7, 1, 2, 8, 4, 5]


*/
int[] array = { 3, 7, 1, 2, 8, 4, 5 };
var expectdedValue = ArrayUtils.findMissingNumber(array);
Console.WriteLine(expectdedValue.ToString());


Console.Read();

public class ArrayUtils
{

	public ArrayUtils() { }

	public static int findMissingNumber(int[] array)
	{

		if (array == null || array.Length == 0) return 0;

		var totalCount = 0;
		var expectedCount = 0;

		//traverse array get total count
		for (int i = 0; i < array.Length; i++)
		{
			totalCount += array[i];
		}

		//get expected count
		expectedCount = getExpectedCount(array.Length+1);

		//get difference
		return expectedCount - totalCount;
	}

	private static int getExpectedCount(int length)
	{

		var expectedCount = 0;
		while (length > 0)
		{
			expectedCount += length;
			length--;
		}
		return expectedCount;
	}
}




