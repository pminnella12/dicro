using System;
using System.Collections.Generic;
using System.Text;

namespace TreeTraversal
{

    public class TreeTraverseExample
    {

        public TreeNode root;

        public TreeTraverseExample()
        {
            this.createATree();
        }

        private void createATree()
        {

            root = new TreeNode(1);

            // Level 2
            root.left = new TreeNode(2);
            root.right = new TreeNode(3);

            // Level 3
            root.left.left = new TreeNode(4);
            root.left.right = new TreeNode(5);
            root.right.left = new TreeNode(6);
            root.right.right = new TreeNode(7);

            // Level 4
            root.left.left.left = new TreeNode(8);
            root.right.left.left = new TreeNode(9);
            root.right.left.right = new TreeNode(10);

            /*
             
                                    1
                                  /    \
                                 2      3
                                / \    /  \
                              4    5  6    7
                             /       /  \
                            8       9    10

            */
        }
    }


    public class BianryTreeExample
    {

        public TreeNode root;

        public BianryTreeExample()
        {
            this.createATree();
        }

        private void createATree()
        {

            root = new TreeNode(26);

            // Level 2
            root.left = new TreeNode(18);
            root.right = new TreeNode(30);

            // Level 3
            root.left.left = new TreeNode(10);
            root.left.right = new TreeNode(21);
            root.right.left = new TreeNode(7);
            root.right.right = new TreeNode(5);

            // Level 4
            root.left.left.left = new TreeNode(8);
            root.left.left.right = new TreeNode(11);
            root.right.right.left = new TreeNode(12);
            root.right.right.right = new TreeNode(2);


            //level 5
            root.left.left.left.left = new TreeNode(3);
            root.left.left.left.right = new TreeNode(4);
            root.left.left.right.left = new TreeNode(6);
            /*


                                    26
                                  /    \
                                 18     30
                                / \    /  \
                              10   21  7    5
                             /  \          /  \
                            8    11       12   2
                           / \   /
                          3   4  6
            */
        }
    }

    public class BianryTreeExample2
    {

        public TreeNode root;

        public BianryTreeExample2()
        {
            this.createATree();
        }

        private void createATree()
        {

            root = new TreeNode(10);

            // Level 2
            root.left = new TreeNode(12);
            root.right = new TreeNode(15);

            // Level 3
            root.left.left = new TreeNode(25);
            root.left.right = new TreeNode(30);
            root.right.left = new TreeNode(36);


            /*


                                    10
                                  /    \
                                 12     15
                                / \    /  \
                              25   30  36 
            */
        }
    }
}