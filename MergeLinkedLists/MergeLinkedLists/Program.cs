// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*
Merge two sorted linked lists
Given two sorted linked lists, merge them so that the resulting linked list is also sorted. 
Consider two sorted linked lists and the merged list below them as an example.

[head1] => [4] => [8] => [15] => [19] => [NULL]
[head2] => [7] => [9] => [10] => [16] => [NULL]

[head1] => [4] => [7] => [8] => [9] => [10] => [15] => [16] => [19] => [NULL]

compare nodes on each list one at time, then transfer in list2 nodes where applicable'
compare if node 2 is greated than node 1 and less than node 1 next node
1.) need to add node 2 to next node of node 1
2.) need to move node 1 next node to next node of node 2
3.) need to move node 2 parent node to next node of node 2 next node

[head1] => [4] => [7]  => [8] => [15] => [19] => [NULL]
[head2] => [9] => [10] => [16] => [NULL]

[head1] => [4] => [7] => [8] => [9] =>  [15] => [19] => [NULL]
[head2] => [10] => [16] => [NULL]

[head1] => [4] => [7] => [8] => [9] => [10] => [15] => [16] => [19] => [NULL]
[head2] => [NULL]

*/

int[] array = { 4, 8, 15, 19 };
int[] array2 = { 7, 9, 10, 16 };
var head = LinkedListUtil.CreateList(array);
var head2 = LinkedListUtil.CreateList(array2);
LinkedListUtil.PrintList(head);

var newList = LinkedListUtil.MergeLinkLists(head, head2);
LinkedListUtil.PrintList(newList);

Console.Read();
public class LinkedListNode
{

	public bool IsHead { get; }
	public int? Data { get; set; }
	public LinkedListNode NextNode { get; set; }
	public LinkedListNode(int? data = null, LinkedListNode nextNode = null)
	{

		if (data == null)
		{
			IsHead = true;
		}
		else
		{
			Data = data;
		}

	   NextNode = nextNode;
	}
}

public static class LinkedListUtil
{

	/*
	[head1] => [4] => [7] =>  [8] => [15*] => [19] => [NULL]
	[head2^] => [9*] => [10] => [16] => [NULL]



	0 < 7 and 7 < 4 false
	4 < 7 and 7 < 8 true
	7 < 8 false 9 < 8 false
...


	*/
	public static LinkedListNode MergeLinkLists(LinkedListNode head, LinkedListNode head2)
	{

		var list1CurrentNode = head;
		var list2CurrentNode = head2.NextNode;
		var currentNode2Parent = head2;
		//compare
		while (list2CurrentNode != null)
		{
			var currentNodeData = list1CurrentNode.Data ?? 0;
			if (currentNodeData < list2CurrentNode.Data && list2CurrentNode.Data < list1CurrentNode.NextNode.Data)
			{
				//merge if it fits
				MergeNode(list1CurrentNode, list2CurrentNode, currentNode2Parent);
				//reset currentnodes
				list1CurrentNode = list2CurrentNode;
				list2CurrentNode = currentNode2Parent.NextNode;

			}
			else
			{
				//move next if not ftis
				list1CurrentNode = list1CurrentNode.NextNode;
			}

		}


		return head;
	}

	private static void MergeNode(LinkedListNode currentNode1, LinkedListNode currentNode2, LinkedListNode currentNode2Parent)
	{

		currentNode2Parent.NextNode = currentNode2.NextNode;
		currentNode2.NextNode = currentNode1.NextNode;
		currentNode1.NextNode = currentNode2;
	}

	public static void PrintList(LinkedListNode head)
	{

		string output = string.Empty;
		var currentNode = head;
		while (currentNode != null)
		{
			if (!currentNode.IsHead)
			{
				if (currentNode.Data != null)
				{
					output += "=> [" + ((int)currentNode.Data).ToString() + "]";
				}
				else
				{
					output += "=> [ ] ";
				}
			}
			if (currentNode.IsHead)
			{
				output += " [HEAD] ";
			}
			currentNode = currentNode.NextNode;
		}
		Console.WriteLine(output);

	}

	public static LinkedListNode CreateList(int[] array)
	{

		var head = new LinkedListNode();
		var currentNode = head;
		for (int i = 0; i < array.Length; i++)
		{
			currentNode.NextNode = new LinkedListNode(array[i]);
			currentNode = currentNode.NextNode;
		}

		return head;

	}
}

