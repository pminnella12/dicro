// See https://aka.ms/new-console-template for more information
using System.Text;

Console.WriteLine("Hello, World!");

var myString = "paaaweelocomeTooPrograaammingiiiiiit";
new StringModifyer().RemoveVowels(myString);

public class StringModifyer {
    private HashSet<char> _vowels;

    public StringModifyer() {
        _vowels = new HashSet<char>();
        BuildVowelList();
    }

    public void RemoveVowels(string myString)
    {
        
        StringBuilder sb = new StringBuilder();

        int len = myString.Length;
        for (int i = 0; i < len; i++) {
            if (_vowels.Contains(myString[i]))
            {
                if (!CheckBefore(myString, i) && !CheckAfter(myString, i)) {
                    Console.Write(myString[i]);
                    sb.Append(myString[i]);
                }

            }
            else {
                Console.Write(myString[i]);
                sb.Append(myString[i]);
            }



        }

        Console.WriteLine("");
        Console.WriteLine(sb.ToString());
    }

    private bool CheckBefore(string myString, int i) {
        bool result = false;

        if (i > 0 && _vowels.Contains(myString[i - 1]))
            result = true;

        return result;
    }

    private bool CheckAfter(string myString, int i)
    {
        bool result = false;

        if (i < (myString.Length-1) && _vowels.Contains(myString[i + 1]))
            result = true;

        return result;
    }

    private void BuildVowelList() {

        _vowels.Add('a');
        _vowels.Add('e');
        _vowels.Add('i');
        _vowels.Add('o');
        _vowels.Add('u');
        _vowels.Add('y');
    }

}
