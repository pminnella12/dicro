// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");



int[] array = new int[] { 4, 5, 6, 7, 8, 9, 10 };
NumberGenerator.GenerateNumbers(array, 6);

/*


Shuffle: Write a method to shuffle a deck of cards. It must be a perfect shuffle-in other words, each of the 52! permutations of the deck has to be equally likely. Assume that you are given a random number generator which is perfect.

*/

//var deck = new Deck();

//deck.Shuffle();

Console.Read();

public class Card
{
	public string Suit { get; }
	public string Value { get; }
	public Card(string suit, string value)
	{
		Suit = suit;
		Value = value;
	}
}


public class Deck
{

	Card[] DeckOfCards { get; }
	string[] Suits { get; }
	string[] CardValues { get; }
	Random rnd;
	public Deck()
	{
		DeckOfCards = new Card[52];
		Suits = new string[] { "Hearts", "Diamonds", "Clubs", "Spades" };
		CardValues = new string[] { "Aces", "2", "3", "4", "5", "6", "7", "8", "9", "10", "Jack", "Queen", "King" };
		CreateDeck();
		//PrintDeck();
		rnd = new Random();

	}

	private void CreateDeck()
	{
		var deckIndex = 0;
		for (int i = 0; i < Suits.Length; i++)
		{
			for (int j = 0; j < CardValues.Length; j++)
			{
				DeckOfCards[deckIndex] = new Card(Suits[i], CardValues[j]);
				deckIndex++;
			}
		}
	}

	private void PrintDeck()
	{
		for (int i = 0; i < DeckOfCards.Length; i++)
		{
			Console.WriteLine(DeckOfCards[i].Value + " of " + DeckOfCards[i].Suit);
		}
	}

	public void Shuffle()
	{

		ShuffleByBook(51);
		PrintDeck();

	}

	public void ShuffleByBook(int i) {
		if (i == 0) { return; }

		// shuffle elements 0 through index - 1
		ShuffleByBook(i - 1);
		int k = rnd.Next(i + 1);

		//swap element k and index
		Card temp = DeckOfCards[k];
		DeckOfCards[k] = DeckOfCards[i];
		DeckOfCards[i] = temp;

		return;
	}

	public void swapCard(int index, Card card, int cardsSwapped, int maxSwapp)
	{
		var card1 = DeckOfCards[index];
		if (card != null)
		{
			DeckOfCards[index] = card;
		}
		var newIndex = rnd.Next(0, 51);
		cardsSwapped++;
		if (cardsSwapped < maxSwapp)
		{
			swapCard(newIndex, card1, cardsSwapped, maxSwapp);
		}
	}
}



/*

Random Set: Write a method to randomly generate a set of m
integers from an array of size n. Each element must have equal probability of being chosen.

*/

public class NumberGenerator
{

	public static void GenerateNumbers(int[] array, int m)
	{

		for (int i = 0; i < m; i++)
		{
			GenerateNumber(array);
		}
	}

	public static void GenerateNumber(int[] array)
	{
		for (int i = 0; i < array.Length; i++)
		{
			var rand = new Random();
			var index = rand.Next(array.Length);
			var temp = array[index];
			array[index] = array[i];
			array[i] = temp;
		}

		System.Text.StringBuilder sb = new System.Text.StringBuilder("");
		for (int j = 0; j < array.Length; j++)
		{
			sb.Append(array[j].ToString());
		}

		Console.WriteLine(sb.ToString());
	}

}




















