/*

LRU Cache

Design and build a "least recently used" cache, which evicts the least recently used item. 
The cache should map from keys to values (allowing you to insert and retreive a value associated with a particular key)
and be initialized with a max size.  When it is full, it should evict the least recently used item.

hash table to store keys, 
maps to linked list maintaining size and removing tail, and appending to head, tail key will be removed
when max length has gone over

*/


// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

LRUCache cache = new LRUCache(3);
cache.AddCacheItem(1, "OLDEST");
cache.AddCacheItem(2, "testing_word");
cache.AddCacheItem(3, "wordle");
cache.AddCacheItem(4, "forth");
cache.AddCacheItem(5, "fifth");
cache.AddCacheItem(6, "sixth");
cache.PrintCache();
Console.Read();

public class LinkedListNode
{
	public int key;
	public LinkedListNode nextNode;
	public LinkedListNode parentNode;
	public LinkedListNode(int v)
	{
		key = v;
		nextNode = null;
		parentNode = null;
	}
}

public class LRUCache
{

	private int _MaxSize;
	private Dictionary<int, string> cacheData;
	LinkedListNode head;
	LinkedListNode tail;

	public LRUCache(int maxSize)
	{
		_MaxSize = maxSize;
		cacheData = new Dictionary<int, string>();
		head = null;
		tail = null;
	}

	public void AddCacheItem(int key, string value)
	{
		if (cacheData.Count() == _MaxSize)
		{
			RemoveTail();
		}

		cacheData.Add(key, value);
		AddNode(key);
	}


	private void AddNode(int key)
	{

		LinkedListNode newNode = new LinkedListNode(key);

		if (head == null)
		{
			head = newNode;
			tail = newNode;
		}
		else
		{
			LinkedListNode tempNode = head;
			head = newNode;
			newNode.nextNode = tempNode;
			tempNode.parentNode = head;
		}

	}

	private void RemoveTail()
	{

		if (tail != null)
		{
			LinkedListNode tempNode = tail;
			int key = tempNode.key;
			cacheData.Remove(key);
			tail = tail.parentNode;
			tempNode = null;
		}
	}

	public void PrintCache()
	{
		foreach (int key in cacheData.Keys)
		{
			Console.WriteLine("key:" + key.ToString() + " value: " + cacheData[key]);
		}
	}
}

