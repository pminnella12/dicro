// See https://aka.ms/new-console-template for more information
using System.Collections;

Console.WriteLine("Hello, World!");

/**************/
/* Dictionary */

Dictionary<string, int> dictionary = new Dictionary<string, int>();
Hashtable hashTable = new Hashtable();


dictionary.Add("test1", 1);
dictionary.Add("test2", 2);

hashTable.Add("test1", 1);
hashTable.Add("test2", 2);


if (dictionary.ContainsKey("test2"))
    dictionary.Remove("test2");

if (hashTable.ContainsKey("test2"))
    hashTable.Remove("test2");

int val1 = dictionary["test1"];
int val2 = (int)hashTable["test1"];
//Console.WriteLine(val1.ToString());

foreach (string key in hashTable.Keys) {

    //Console.WriteLine(hashTable[key]);
}


/************************/
/*    List & ArrayList  */

List<int> list = new List<int>();

ArrayList arrayList = new ArrayList();

list.Add(1);
list.Add(2);
list.Add(3);
list.Add(1);

arrayList.Add(1);
arrayList.Add(2);
arrayList.Add(3);
arrayList.Add(1);

list.Count();
var count = arrayList.Count;

list.RemoveAt(1);
arrayList.RemoveAt(1);

list.Insert(1, 2);
arrayList.Insert(1, 2);

list.Contains(2);
arrayList.Contains(2);


list.Remove(3);
arrayList.Remove(3);
foreach (int item in list)
{

    //Console.WriteLine(item.ToString());
}


foreach (int item in arrayList)
{

    //Console.WriteLine(item.ToString());
}



/************************/
/*       HashSet        */

HashSet<int> hashSet = new HashSet<int>();
HashSet<int> hashSet2 = new HashSet<int>();
hashSet.Add(1);
hashSet.Add(1);
hashSet.Add(6);
hashSet.Add(4);
hashSet.Add(2);
hashSet.Add(3);
hashSet.Add(5);

hashSet2.Add(4);
hashSet2.Add(5);
hashSet2.Add(7);
hashSet2.Add(6);
hashSet2.Add(22);
hashSet2.Add(33);
hashSet2.Add(55);

hashSet.Count();
hashSet.Contains(6);
var min = hashSet.Min();
var max = hashSet.Max();

//merges
hashSet.UnionWith(hashSet2);

//items in both
hashSet.IntersectWith(hashSet2);

//adds at beginning
hashSet.Prepend(4);

hashSet.Remove(1);


foreach (int item in hashSet)
{
    //Console.WriteLine(item.ToString());
}




/*******************************/
/*             QUEUE           */

Queue<int> queue = new Queue<int>();
queue.Enqueue(1);
queue.Enqueue(55);
queue.Enqueue(2);
queue.Enqueue(3);

int result;
int result2;
queue.TryDequeue(out result);
if (queue.TryPeek(out result2)) { bool exists = true; }

var result3 = queue.Peek();

queue.Contains(55);
queue.Count();
var output = queue.Dequeue();

//queue.Clear();
//result3 = queue.Peek(); ERROR
//output = queue.Dequeue(); ERROR


//Console.WriteLine(result.ToString());
//Console.WriteLine(result2.ToString());



/*******************************/
/*            STACK            */

Stack<int> stack = new Stack<int>();

stack.Push(1);
stack.Push(2);
stack.Push(3);
stack.Push(4);


stack.TryPeek(out val1);
stack.TryPop(out val1);

//stack.Clear();
//val2 = stack.Pop();  ERROR
//val2 = stack.Peek(); ERROR

stack.Count();
stack.Contains(5);
Console.Read();


