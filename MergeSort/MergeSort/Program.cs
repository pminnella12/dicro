// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

public class MergeSortUtil {

    public static void MergeSort(int[] array) {

        int[] helper = new int[array.Length];
        MergeSort(array, helper, 0, array.Length - 1);
    }

    public static void MergeSort(int[] array, int[] helper, int low, int high) {

        if (low < high) {

            int middle = (low + high) / 2;
            MergeSort(array, helper, low, middle); //Sort left Half
            MergeSort(array, helper, middle + 1, high); //Sort top Half
            Merge(array, helper, low, middle, high); //Merge them
        }
    }

    public static void Merge(int[] array, int[] helper, int low, int middle, int high) {

        /* Copy both halves into helper array */
        for (int i = low; i <= high; i++) {
            helper[i] = array[i];
        }

        int helperLeft = low;
        int helperRight = middle + 1;
        int current = low;

        /* Iterate through helper array.  Compare the left and the right half, copying back
         * the smaller element from the two halves int the original array. */

        while (helperLeft <= middle && helperRight <= high) {
            if (helper[helperLeft] <= helper[helperRight])
            {
                array[current] = helper[helperLeft];
                helperLeft++;
            }
            else { //if right element is smaller than left element
                array[current] = helper[helperRight];
                helperRight++;

            }
            current++;

        }

        /* copy the rest of the left side of the array into the target array */
        int remaining = middle - helperLeft;
        for (int i = 0; i <= remaining; i++) {
            array[current + i] = helper[helperLeft + i];
        }
    }
}