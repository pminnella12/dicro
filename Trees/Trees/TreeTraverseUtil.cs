
using System;
using System.Collections.Generic;
using System.Text;

namespace TreeTraversal
{
    public class TreeTraverseUtil
    {


        /*

                1
             /    \
             2      3
           / \    /  \
         4    5  6    7
        /       /  \
        8       9    10

        8 4 2 5 1 9 6 10 3 7
        Used to get the values of the nodes in non-decreasing order in a BST.

        An inorder traversal does an inorder traversal on the left subtree,
        followed by a visit to the root node, followed by an inorder traversal
        of the right subtree.

        If you know that the tree has an inherent sequence in the nodes, and you
        want to flatten the tree back into its original sequence, than an in-order
        traversal should be used. The tree would be flattened in the same way it
        was created. A pre-order or post-order traversal might not unwind the
        tree back into the sequence which was used to create it.
        */
        public void inOrderTraversal(TreeNode root)
        {
            if (root != null)
            {
                inOrderTraversal(root.left);
                Console.WriteLine(root.data + " ");
                inOrderTraversal(root.right);
            }
        }



        /*

                1
              /    \
             2      3
           / \    /  \
         4    5  6    7
        /       /  \
        8       9    10

        1 2 4 8 5 3 6 9 10 7
        Used to create a copy of a tree. For example, if you want to create a replica of a tree,
        put the nodes in an array with a pre-order traversal. Then perform an Insert operation
        on a new tree for each value in the array. You will end up with a copy of your original tree.

        A preorder traversal visits the root node first, followed by a preorder traversal of the
        left subtree, followed by a preorder traversal of the right subtree.

        If you know you need to explore the roots before inspecting any leaves, you pick pre-order
        because you will encounter all the roots before all of the leaves.
        */
        /** Preorder Traversal **/
        private void preorderTraversal(TreeNode root)
        {
            if (root != null)
            {
                Console.WriteLine(root.data + " ");
                preorderTraversal(root.left);
                preorderTraversal(root.right);
            }
        }



        /*

                1
             /    \
            2      3
           / \    /  \
         4    5  6    7
        /       /  \
        8       9    10

        8 4 5 2 9 10 6 7 3 1
        Used to delete a tree from leaf to root

        A postorder traversal does a postorder traversal of the left subtree,
        followed by a postorder traversal of the right subtree, followed by a
        visit to the root node.

        If you know you need to explore all the leaves before any nodes, you
        select post-order because you don't waste any time inspecting roots in search for leaves.
        */
        /** Postorder Traversal **/
        private void postorderTraversal(TreeNode root)
        {
            if (root != null)
            {
                postorderTraversal(root.left);
                postorderTraversal(root.right);
                Console.WriteLine(root.data + " ");
            }
        }



        /*

                1
             /    \
            2      3
           / \    /  \
         4    5  6    7
        /       /  \
        8       9    10

        1 2 3 4 5 6 7 8 9 10

        */
        /** Level Order Traversal **/
        private void levelorderTraversal(TreeNode root)
        {
            if (root == null)
            {
                return;
            }

            Queue<TreeNode> queue = new Queue<TreeNode>();
            queue.Enqueue(root);

            while (queue.Count != 0)
            {
                TreeNode node;

                if (queue.TryDequeue(out node))
                {
                    Console.WriteLine(node.data + " ");

                    if (node.left != null)
                    {
                        queue.Enqueue(node.left);
                    }

                    if (node.right != null)
                    {
                        queue.Enqueue(node.right);
                    }
                }
            }
        }

        /** Print Functions **/
        public void printInorderTraversal(TreeNode root)
        {
            Console.WriteLine("Inorder: ");
            inOrderTraversal(root);
            Console.WriteLine("");
        }

        public void printPreorderTraversal(TreeNode root)
        {
            Console.WriteLine("Preorder: ");
            preorderTraversal(root);
            Console.WriteLine("");
        }

        public void printPostorderTraversal(TreeNode root)
        {
            Console.WriteLine("Postorder: ");
            postorderTraversal(root);
            Console.WriteLine("");
        }

        public void printLevelorderTraversal(TreeNode root)
        {
            Console.WriteLine("Levelorder: ");
            levelorderTraversal(root);
            Console.WriteLine("");
        }
    }
}