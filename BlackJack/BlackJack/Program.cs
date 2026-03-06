// See https://aka.ms/new-console-template for more information
using System.Collections;

Console.WriteLine("Hello, World!");

public enum Suit {
    Club = 0,
    Diamond = 1,
    Heart = 2,
    Spade = 3
}

public abstract class Card {

    private bool available = true;

    /* number or face that's on card - a number 2 through 10 
     * 11 for jack
     * 12 for queen
     * 13 for king
     * 1 for ace
     */

    protected int faceValue;
    protected Suit suit;

    public Card(int v, Suit s) {
        faceValue = v;
        suit = s;
    }

    public abstract int value();
    public Suit getSuit() { return suit;  }

    /* checks if card is available to be given out to someone */
    public bool isAvailable() { return available; }
    public void markUnavailable() { available = false; }
    public void markAvailable() { available = true; }
}

public class Deck<T> where T: Card {

    private List<T> cards; //all cards, dealt or not
    private int dealtIndex = 0; // marks first undealt card

    public void setDeckOfCards(List<T> deckOfCards) {
        cards = deckOfCards;
    }

    public void shuffle() {
        var rnd = new Random();
        cards = cards.OrderBy(item => rnd.Next()).ToList();

    }

    public int remainingCards() {
        return cards.Count - dealtIndex;
    }

    public IEnumerable<T> dealHand(int number) { return cards.Take(number).Where(x => x.isAvailable()); }
    public T? dealCard() { return cards.Take(1).Where(x => x.isAvailable()).FirstOrDefault(); }
}

public class Hand<T> where T : Card {
    protected List<T> cards = new List<T>();


    public int score() {
        int score = 0;
        foreach (var card in cards) {

            score += card.value();
        }
        return score;
    }

    public void addCard(T card) {
        cards.Add(card);
    }

}

public class BlackJackCard : Card
{
    public BlackJackCard(int v, Suit s) : base(v, s) { }

    
    public override int value()
    {
        if (isAce()) return 1;
        else if (faceValue > 11 && faceValue <= 13) return 10;
        else return faceValue;
    }

    public bool isAce() {
        return faceValue == 1;
    }

    public int minValue() {
        if (isAce()) return 1;
        else return value();
    }

    public int maxValue()
    {
        if (isAce()) return 11;
        else return value();
    }

    public Boolean isFaceCard() {
        return faceValue >= 11 && faceValue <= 13;
    }
}

public class BlackJackHand : Hand<BlackJackCard> {
    /* There are multiple possible scores for a blackjack hand, since aces have 
     * multiple values. Return the highest possible score that's under 21, or the 
     * lowest score that's over */

    public int score() {
        List<int> scores = new List<int>();
        int maxUnder = int.MinValue;
        int minOver = int.MaxValue;

        foreach (int sc in scores) {
            if (sc > 21 && sc < minOver)
            {
                minOver = sc;
            }
            else if (sc <= 21 && sc > maxUnder) {
                maxUnder = sc;
            }
        }

        return maxUnder == int.MinValue ? minOver : maxUnder;

    }

    /* return a list of all possible scores this hand could have (evaulating each
     * ace as both 1 and 11 */

    //private List<int> possibleSCores() { }
    public Boolean busted() { return score() > 21; }
    public Boolean is21() { return score() == 21; }
    public Boolean isBlackJack() { return score() == 21 && cards.Count == 2; }


}