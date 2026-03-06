using System;
using System.Collections.Generic;
using System.Text;

namespace TreeTraversal
{
    public class TreeNode
    {
        public int data;
        public TreeNode left;
        public TreeNode right;

        public TreeNode(int data)
        {
            this.data = data;
            this.left = this.right = null;
        }
    }
}
