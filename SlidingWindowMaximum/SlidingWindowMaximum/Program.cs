// See https://aka.ms/new-console-template for more information
using System.Collections;

Console.WriteLine("Hello, World!");

/*

sliding window maximum


Problem Statement: Sliding Window Maximum
sliding window maximum : Given an array and an integer K in sliding window maximum problem, find the maximum for each and every contiguous subarray of size k.
Examples:

arr[] = [1, 2, 3, 1, 4, 5, 2, 3, 6]

K = 3

Output: 3 3 4 5 5 5 6

Explanation

Maximum of 1, 2, 3 is 3

Maximum of 2, 3, 1 is 3

Maximum of 3, 1, 4 is 4

Maximum of 1, 4, 5 is 5

Maximum of 4, 5, 2 is 5

Maximum of 5, 2, 3 is 5

Maximum of 2, 3, 6 is 6



*/
//TODO Queue need to be changed to List instead so you can Dequeue, (RemoveAt(0) and also RemoveAt(End))
int[] arr = {1, 2, 3, 1, 4, 5, 2, 3, 6};

SlindingWindowMaximum.GetSlidWindowMax(arr, 3);

public class SlindingWindowMaximum
{

	public static Queue<int> queue = new Queue<int>();
	public static List<int> results = new List<int>();

	public static void GetSlidWindowMax(int[] arr, int k)
	{

		int j = 0;
		int highest = 0;
		for (int i = 0; i < arr.Length - 3; i++)
		{

			if (queue.Count() > 0)
			{
				queue.Dequeue();
			}

			if (j >= k)
			{
				if (queue.Count() == 0 || queue.Peek() < arr[j])
				{
					queue.Enqueue(arr[j]);
					highest = arr[j];
				}
				else {
					highest = queue.Peek();
				}
				j++;
			}


			while (j < k)
			{
				if (queue.Count() == 0 || queue.Peek() < arr[j])
				{
					queue.Enqueue(arr[j]);
					highest = arr[j];
				}
				j++;
			}

			
			results.Add(highest);
			highest = 0;

		}

		foreach (int i in results) {
			Console.WriteLine(i.ToString());
		}
	}
}


