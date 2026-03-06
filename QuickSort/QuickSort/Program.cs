// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


int[] array = new int[4];

Console.WriteLine(array[0].ToString());
Console.WriteLine("End");
/*
left = 0
right= 6
--------
[ 0, 1, 2, 3, 4, 5, 6]
[ 2, 4, 3, 1, 5, 8, 7]
pivot = 5
l-4
r-3

index = 4
0<3 //sort left half
    [ 0, 1, 2, 3, 4, 5, 6]
    [ 2, 4, 3, 1, 5, 8, 7]
    left-0, right-3
    pivot-2
    left1, right-3
    [ 0, 1, 2, 3, 4, 5, 6]
    [ 2, 1, 3, 4, 5, 8, 7]

3<6 //sort right half
    [ 0, 1, 2, 3, 4, 5, 6]
    [ 2, 4, 3, 1, 5, 8, 7]
    left-4, right-6
    pivot-5
    left5, right7
    [ 0, 1, 2, 3, 4, 5, 6]
    [ 2, 4, 3, 1, 5, 7, 8]

*/
public class QuickSortUtil {

    public static void QuickSort(int[] arr, int left, int right) {

        int index = Partition(arr, left, right);
        if (left < index - 1) { //sort left half
            QuickSort(arr, left, index - 1);
        }
        if (index < right) { //sort right half
            QuickSort(arr, index, right);
        }
    }



    private static int Partition(int[] arr, int left, int right) {
        int pivot = arr[(left + right) / 2]; //pick a pivot point
        while (left <= right) {
            //find element on the left that should be on the right
            while (arr[left] < pivot) left++;

            //find element on right that should be on left
            while (arr[right] > pivot) right--;

            //swap element, and move left and right indices
            if (left <= right) {
                Swap(arr, left, right); //swaps elements
                left++;
                right--;
            }
        }
        return left;

    }

    private static void Swap(int[] arr, int left, int right) {

        var leftValue = arr[left];
        var rightValue = arr[right];

        arr[left] = rightValue;
        arr[right] = leftValue;
    }
}