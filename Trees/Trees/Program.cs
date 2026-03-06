using System;
using Trees;

namespace TreeTraversal
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            /*
            TreeTraverseExample treeTraversalExample = new TreeTraverseExample();

            TreeTraverseUtil util = new TreeTraverseUtil();
            Console.WriteLine("------------------------------------");
            util.printInorderTraversal(treeTraversalExample.root);
            Console.WriteLine("------------------------------------");
            util.printPreorderTraversal(treeTraversalExample.root);
            Console.WriteLine("------------------------------------");
            util.printPostorderTraversal(treeTraversalExample.root);
            Console.WriteLine("------------------------------------");
            util.printLevelorderTraversal(treeTraversalExample.root);
            Console.WriteLine("------------------------------------");


            BianryTreeExample binaryTreeExample = new BianryTreeExample();

            SpecialSum.GetSpecialSum(binaryTreeExample.root);
            CommonAncestor ca = new CommonAncestor(binaryTreeExample.root, 3, 6);
            */
            BianryTreeExample2 binaryTreeExample2 = new BianryTreeExample2();
            ClassTreeConverter convert = new ClassTreeConverter();
            convert.ConvertToList(binaryTreeExample2.root);

            Console.Read();
        }
    }
}
