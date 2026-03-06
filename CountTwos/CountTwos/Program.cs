// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*

Count of 25: Write a method to count the number of 2s that appear in all the numbers between 0 and n (inclusive).
EXAMPLE
Input: 25
Output: 9 (2, 12,20, 21,22, 23,24 and 25. Note that 22 counts for two 2s.)

25

twenty - 7
teens - 1
single -1


225
hundreds- 27 + 11


*/


var count = CountTwos.countTwosBruteForce(25);
Console.WriteLine(count.ToString());
Console.Read();


public class CountTwos
{

	//public static int countTwosOptimized(int number) {


	//}


	public static int countTwosBruteForce(int number)
	{
		var count = 0;
		for (int i = 0; i <= number; i++)
		{
			char[] array = i.ToString().ToCharArray();
			for (int j = 0; j < array.Length; j++)
			{
				if (array[j] == '2')
				{
					count++;
				}
			}
		}

		return count;
	}
}