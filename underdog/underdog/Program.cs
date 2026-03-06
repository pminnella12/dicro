


List<int> myInts = new List<int> {
    1, 5, 66, 33, 2
};

var resultsOne = myInts.Where(x => x > 5);

myInts = null;
var resultsTwo = myInts.Where(x => x > 5);


// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");



