// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*
	2. Determine if the sum of two integers is equal to the given value

	Given an array of integers and a value, determine if there are any two integers in the array whose sum is equal to the given value.
   Return true if the sum exists and return false if it does not. Consider this array and the target sums:	

	5, 7, 1, 2, 8, 4, 3

	target = 10 7+3=10 2+8=10
	target = 19 none



*/

int[] array = { 5, 7, 1, 2, 8, 4, 3 };
Console.WriteLine(ArrayUtil.sumTwoNumbers(array, 10).ToString());
Console.WriteLine(ArrayUtil.sumTwoNumbers(array, 19).ToString());
Console.Read();
public static class ArrayUtil
{


	public static string sumTwoNumbers(int[] array, int target)
	{

		//go through array store value in dictionary with needed value to hit target
		//check if needed value in dictionary exists - if so found pair

		string foundPairs = string.Empty;
		Dictionary<int, int> lookup = new Dictionary<int, int>();

		for (int i = 0; i < array.Length; i++)
		{

			var neededVal = target - array[i];

			if (lookup.ContainsKey(neededVal))
			{
				foundPairs += " " + neededVal.ToString() + " + " + array[i].ToString();
			}

			lookup.Add(array[i], neededVal);
		}

		return foundPairs;
	}

}