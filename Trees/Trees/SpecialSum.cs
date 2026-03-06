using System;
using TreeTraversal;

namespace Trees
{
    public class SpecialSum
    {
        public SpecialSum()
        {
        }

        public static Dictionary<string, int> nodeSpecialSum = new Dictionary<string, int>();

        public static List<int> GetSpecialSum(TreeNode node) {
            List<int> specialSumNodes = new List<int>();

            TraverseTree(node, specialSumNodes);

            foreach (int nodeValue in specialSumNodes) {
                Console.WriteLine(nodeValue.ToString());
            }

            return specialSumNodes;
        }


        private static void TraverseTree(TreeNode node, List<int> specialSumsNodes) {
            if (node == null) return;

            if (IsNodeSpecialSum(node)) { specialSumsNodes.Add(node.data); }
            TraverseTree(node.left, specialSumsNodes);
            TraverseTree(node.right, specialSumsNodes);

        }

        private static bool IsNodeSpecialSum(TreeNode node) {
            int leftTotal = 0;
            int rightTotal = 0;

            string leftKey = node.data.ToString() + "left";
            string rightKey = node.data.ToString() + "right";
            if (nodeSpecialSum.ContainsKey(leftKey))
            {
                Console.WriteLine("leftCache hit");
                leftTotal = nodeSpecialSum[leftKey];
            }
            else {
                leftTotal = GetLeftTotal(node.left, ref leftTotal);
            }

            if (nodeSpecialSum.ContainsKey(rightKey))
            {
                Console.WriteLine("rightCache hit");
                rightTotal = nodeSpecialSum[rightKey];
            }
            else {
                rightTotal = GetRightTotal(node.right, ref rightTotal);
            }            

            if (leftTotal == rightTotal && leftTotal > 0) {
                return true;
            }

            return false;
        }

        private static int GetLeftTotal(TreeNode node, ref int total) {
            if (node == null) return total;
            string leftKey = node.data.ToString() + "left";
            GetLeftTotal(node.left, ref total);
            nodeSpecialSum[leftKey] = total;
            
            total += node.data;

            return total;           
        }

        private static int GetRightTotal(TreeNode node, ref int total)
        {
            if (node == null) return total;
            string rightKey = node.data.ToString() + "right";
            GetRightTotal(node.right, ref total);
            nodeSpecialSum[rightKey] = total;
            
            total += node.data;


            return total;

        }
    }
}

