using System;
using TreeTraversal;

namespace Trees
{
    public class LinkedListNode {

        public int Data { get; set; }
        public LinkedListNode PrevNode;
        public LinkedListNode NextNode;
        public LinkedListNode(int value) {
            Data = value;
            PrevNode = null;
            NextNode = null;
        }
    }


    public class ClassTreeConverter {

        public LinkedListNode Head;
        public LinkedListNode Tail;

        public void ConvertToList(TreeNode node) {

            Head = null;
            Tail = null;

            TraverseInOrder(node);

            Console.Read();
        }

        private void TraverseInOrder(TreeNode node) {

            if (node == null) return;


            TraverseInOrder(node.left);
            AddNodeToList(node);
            TraverseInOrder(node.right);
        }

        private void AddNodeToList(TreeNode node) {

            if (Head == null)
            {
                Head = new LinkedListNode(node.data);
                Tail = null;
            }
            else if (Tail == null)
            {
                Tail = new LinkedListNode(node.data);
                Head.NextNode = Tail;
                Tail.PrevNode = Head;

            }
            else {
                LinkedListNode temp = Tail;
                Tail = new LinkedListNode(node.data);
                temp.NextNode = Tail;
                Tail.PrevNode = temp;
            }
        }

    }
}

