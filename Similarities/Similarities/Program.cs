using System.Collections;


// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


/*
Sparse Similarity: The similarity of two documents (each with distinct words) is defined to be the size of the intersection divided by the size of the union. For example, if the documents consist of integers,thesimilarityof{1, 5, 3}and{1, 7, 2, 3}ise.4,becausetheintersectionhassize 2 and the union has size 5.
We have a long list of documents (with distinct values and each with an associated ID) where the similarity is believed to be "sparse:'That is, any two arbitrarily selected documents are very likely to have similarity O. Design an algorithm that returns a list of pairs of document IDs and the associated similarity.
Print only the pairs with similarity greater than O. Empty documents should not be printed at all. For simplicity, you may assume each document is represented as an array of distinct integers.
EXAMPLE Input:
13: {14, 15, 100, 9, 3} 
16: {32, 1, 9, 3, 5}
19: {15, 29, 2, 6, 8, 7} 
24: {7, 10}
Output:
ID1, ID2 SIMILARITY
13, 19 a.1
13, 16 a.25
19, 24 a.14285714285714285


*/


int[] arry1 = { 14, 15, 100, 9, 3 };
Document doc1 = new Document(13, arry1.ToList());

int[] arry2 = { 32, 1, 9, 3, 5 };
Document doc2 = new Document(16, arry2.ToList());


int[] arry3 = { 15, 29, 2, 6, 8, 7 };
Document doc3 = new Document(19, arry3.ToList());


int[] arry4 = { 7, 10 };
Document doc4 = new Document(24, arry4.ToList());

List<Document> docs = new List<Document>();
docs.Add(doc1);
docs.Add(doc2);
docs.Add(doc3);
docs.Add(doc4);

Dictionary<int, Document> documents2 = new Dictionary<int, Document>();
documents2[13] = doc1;
documents2[16] = doc2;
documents2[19] = doc3;
documents2[24] = doc4;
//var sims = Similarities.computeSimilaritiesBruteForce(docs);

//TODO DOES NOT WORK 
var sims = Similarities.computeSimilaritiesOptimized(documents2);
Console.Read();

public class DocPair
{
	public int doc1, doc2;

	public DocPair(int d1, int d2)
	{
		doc1 = d1;
		doc2 = d2;
	}

	public override bool Equals(Object o)
	{
		if (o.GetType() == typeof(DocPair)) {
			DocPair p = (DocPair)o;
			return p.doc1 == doc1 && p.doc2 == doc2;
		}
		return false;
	}
	public override int GetHashCode() { return (doc1 * 31) ^ doc2; }
}

public class Document
{
	private List<int> words;
	private int docId;

	public Document(int id, List<int> w)
	{
		docId = id;
		words = w;
	}

	public List<int> getWords() { return words; }
	public int getId() { return docId; }
	public int size() { return words == null ? 0 : words.Count(); }
}

public class Similarities
{
	public static Dictionary<DocPair, double> computeSimilaritiesOptimized(Dictionary<int, Document> documents) {

		Dictionary<int, List<int>> wordToDocs = groupWords(documents);
		Dictionary<DocPair, double> similarities = computeIntersections(wordToDocs);
		adjustToSimilarities(documents, similarities);
		return similarities;
		
	}

	public static Dictionary<DocPair, double> computeIntersections(Dictionary<int, List<int>> wordsToDoc) {
		Dictionary<DocPair, double> similarities = new Dictionary<DocPair, double>();
		foreach (int word in wordsToDoc.Keys) {
			List<int> docs = wordsToDoc[word];
			for (int i = 0; i < docs.Count(); i++) {
				for (int j = i + 1; j < docs.Count(); j++) {
					increment(similarities, docs[i], docs[j]);
				}
			}
		}
		return similarities;
	}

	public static void increment(Dictionary<DocPair, double> similarities, int doc1, int doc2) {

		DocPair pair = new DocPair(doc1, doc2);
		if (!similarities.ContainsKey(pair))
		{
			similarities.Add(pair, 1.0);
		}
		else {
			similarities.Add(pair, similarities[pair] + 1);
		}
	}

	public static void adjustToSimilarities(Dictionary<int, Document> documents, Dictionary<DocPair, double> similarities) {

		foreach (DocPair docPair in similarities.Keys) {

			double intersection = similarities[docPair];
			Document doc1 = documents[docPair.doc1];
			Document doc2 = documents[docPair.doc2];
			double union = (double)doc1.size() + doc2.size() - intersection;
			similarities[docPair] = (intersection/union);
		}
	}


	//create hash table from each word to where it appears
	public static Dictionary<int, List<int>> groupWords(Dictionary<int, Document> documents) {

		Dictionary<int, List<int>> wordToDocs = new Dictionary<int, List<int>>();

		foreach (Document doc in documents.Values) {

			List<int> words = doc.getWords();
			foreach (int word in words) {
				if (wordToDocs.ContainsKey(word))
				{
					var docIds = wordToDocs[word];
					docIds.Add(doc.getId());
					wordToDocs[word] = docIds;
				}
				else
				{
					List<int> docIds = new List<int>();
					wordToDocs.Add(word, docIds);
				}
			}
		}

		return wordToDocs;

	}

	public static Dictionary<DocPair, double> computeSimilaritiesBruteForce(List<Document> documents)
	{
		Dictionary<DocPair, double> similarities = new Dictionary<DocPair, double>();
		for (int i = 0; i < documents.Count(); i++)
		{
			for (int j = i + 1; j < documents.Count(); j++)
			{
				Document doc1 = documents[i];
				Document doc2 = documents[j];
				double sim = computeSimilarity(doc1, doc2);
				if (sim > 0)
				{
					DocPair pair = new DocPair(doc1.getId(), doc2.getId());
					similarities.Add(pair, sim);
				}
			}
		}

		return similarities;
	}

	public static double computeSimilarity(Document doc1, Document doc2)
	{

		int intersection = 0;
		HashSet<int> set1 = new HashSet<int>(doc1.getWords());

		foreach (int word in doc2.getWords())
		{
			if (set1.Contains(word)) {
				intersection++;
			}
		}

		double union = doc1.size() + doc2.size() - intersection;
		return intersection / union;
	}
}
