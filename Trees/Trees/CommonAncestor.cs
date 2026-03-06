using System;
using TreeTraversal;

namespace Trees
{
    public class CommonAncestor
    {
        int[] val1List = null;
        int[] val2List = null;
        public CommonAncestor(TreeNode node, int val1, int val2)
        {
            var output = LowestCommonAncestor(node, val1, val2);

            Console.Read();
        }

        public TreeNode LowestCommonAncestor(TreeNode root, int val1, int val2) {

            if (root == null || root.data == val1 || root.data == val2) {
                return root;
            }
            

            TreeNode left = LowestCommonAncestor(root.left, val1, val2);
            TreeNode right = LowestCommonAncestor(root.right, val1, val2);

            if (left == null)
            {

                return right;
            }
            else if (right == null)
            {
                return left;
            }
            else
            {
                return root;
            }

        }
    }
}

