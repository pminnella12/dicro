// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*

Calculator - Given an arithmetric equation consisting of positive integers + - * and / no parenthsis
compute the result

2*3+5/6*3+15

6 11 1.8333 5.5
 output 23.5

parse string
store * and / in queue then add them togather




stack
-----------------
 15 2.5 6 		   + +
		  	
 */
string s = "2*3+5/6*3+15";
var output = SimpleMath.computeString(s);
Console.WriteLine(output);
Console.Read();

public enum operand
{
	none,
	add,
	subtract,
	multiply,
	divide
}

public class SimpleMath
{

	//2*3+5/6*3+15

	public static Stack<decimal> numbersStack = new Stack<decimal>();
	public static Stack<operand> opStack = new Stack<operand>();

	public static decimal computeString(string equation)
	{

		char[] chars = equation.ToCharArray();
		string currentNumber = "";

		for (int i = 0; i < chars.Length; i++)
		{
			if (Char.IsNumber(chars[i]))
			{
				currentNumber += chars[i];
			}

			if (IsOperand(chars[i]))
			{
				numbersStack.Push(getNumber(currentNumber));
				currentNumber = "";
				operand currentOperand = getOperand(chars[i]);
				if (opStack.Count() > 0)
				{
					//if we have an operand already in here, check priority, and perhaps pop stack

					if (currentOperand == operand.add || currentOperand == operand.subtract)
					{
						popStacks();
						opStack.Push(currentOperand);
					}

					if (currentOperand == operand.multiply || currentOperand == operand.divide)
					{
						if (opStack.Peek() == operand.multiply || opStack.Peek() == operand.divide)
						{
							popStacks();
							opStack.Push(currentOperand);
						}
						if (opStack.Peek() == operand.add || opStack.Peek() == operand.subtract)
						{
							opStack.Push(currentOperand);
						}

					}

				}
				else
				{
					opStack.Push(currentOperand);
				}

			}

		}

		if (currentNumber != "") {
			numbersStack.Push(getNumber(currentNumber));
		}

		while (numbersStack.Count() >= 2 && opStack.Count() >= 1)
		{
			popStacks();
		}

		return numbersStack.Pop();
	}

	// we want to pop last to number and apply the last operand pushed in stack
	// then push the calculated number back in stack
	private static void popStacks()
	{
		if (numbersStack.Count() >= 2 && opStack.Count() >= 1)
		{
			decimal number2 = numbersStack.Pop();
			decimal number1 = numbersStack.Pop();
			operand op = opStack.Pop();

			if (op == operand.add)
			{
				numbersStack.Push(number1 + number2);
			}

			if (op == operand.subtract)
			{
				numbersStack.Push(number1 - number2);
			}

			if (op == operand.divide)
			{
				numbersStack.Push(number1 / number2);
			}

			if (op == operand.multiply)
			{
				numbersStack.Push(number1 * number2);
			}
		}
	}

	private static int getNumber(string currentNumber)
	{
		int num = 0;
		Int32.TryParse(currentNumber, out num);

		return num;
	}

	private static bool IsOperand(char c)
	{
		if (c == '+' || c == '-' || c == '*' || c == '/')
			return true;

		return false;
	}

	private static operand getOperand(char c)
	{
		switch (c)
		{
			case '-': return operand.subtract;
			case '+': return operand.add;
			case '/': return operand.divide;
			case '*': return operand.multiply;
			default: return operand.none;

		}

	}

}