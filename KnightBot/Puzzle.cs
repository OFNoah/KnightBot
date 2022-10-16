using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightBot;

/// Perspective as white. The FEN notation.
/// 
/// 8 [r][n][b][q][k][b][n][r]
/// 7 [p][p][p][p][p][p][p][p]
/// 6 [-][-][-][-][-][-][-][-]
/// 5 [-][-][-][-][-][-][-][-]
/// 4 [-][-][-][-][-][-][-][-]
/// 3 [-][-][-][-][-][-][-][-]
/// 2 [P][P][P][P][P][P][P][P]
/// 1 [R][N][B][Q][K][B][N][R]
///   -a--b--c--d--e--f--g--h-
///
/// rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
/// 
/// Only interested in these 2 fields for the purposes of this implementation:
///  - First field details board position.
///  - Second field details current player's turn, white or black (w/b).
 
/// <summary>
/// Handles chess board position.
/// Retrieves puzzles and stores puzzle state per Discord channel.
/// </summary>
internal class Puzzle
{
    private bool whitesTurn;
    private static Random rand = new Random();

    public string boardPosition;
    public string playingAs;
    public string nextMoves; // moves in UCI to complete the puzzle
    public string puzzleRating; // elo rating representing puzzle difficulty

    // Keys to retrieve a random puzzle
    private int randomPartitionKey; // represents a selection of sort keys. 1 PK contains 30 SKs
    private int randomSortKey; // one random chosen key from the particular partition key


    /// <summary>
    /// Creates a puzzle board position and the moves necessary to complete it.
    /// </summary>
    /// <param name="rating"> Preferred ELO rating. </param>
    public Puzzle(int rating)
    {
        randomPartitionKey = GetRandomPartitionKey(rating);
        randomSortKey = rand.Next(1, 31);

        Task<QueryResponse> queryResponse = QueryPuzzleTable(randomPartitionKey);
        queryResponse.Wait();

        string fen = queryResponse.Result.Items[randomSortKey]["FEN"].S;
        nextMoves = queryResponse.Result.Items[randomSortKey]["Moves"].S;
        puzzleRating = queryResponse.Result.Items[randomSortKey]["Rating"].S;


        string[] splitFenArr = fen.Split(" ");
        boardPosition = splitFenArr[0];

        whitesTurn = splitFenArr[1].Equals("w"); // first turn is for the "opponent"
        playingAs = !whitesTurn ? "white" : "black"; // opposite of opponent's
    }

    /// <summary>
    /// Used to retrieve priorly instantiated puzzles from the saved state database.
    /// </summary>
    private Puzzle(string boardPosition, string nextMoves, string playingAs)
    {
        this.boardPosition = boardPosition;
        this.nextMoves = nextMoves;
        this.playingAs = playingAs;
        this.puzzleRating = ""; // irrelevant in retrievals
    }

    /// <returns> The puzzle state being played in the channel provided.
    ///  Returns null if no puzzle found in channel. </returns>
    public static async Task<Puzzle> RetrievePuzzle(string channelId)
    {
        AmazonDynamoDBClient ddbClient = new AmazonDynamoDBClient();
        var checkChannelRequest = new QueryRequest
        {
            TableName = "ChessPuzzleStates",
            KeyConditionExpression = "ChannelId = :" + channelId,
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":" + channelId, new AttributeValue { S = channelId }}
                }
        };
        QueryResponse dbResponse = await ddbClient.QueryAsync(checkChannelRequest);

        if (dbResponse.Count == 0)
            return null;

        string boardPosition = dbResponse.Items[0]["FEN"].S;
        string nextMoves = dbResponse.Items[0]["Moves"].S;
        string playingAs = dbResponse.Items[0]["PlayingAs"].S;

        return new Puzzle(boardPosition,
                          nextMoves,
                          playingAs);
    }

    /// <summary>
    /// Changes the puzzle's turn and board position. Assumes a legal move is provided.
    /// </summary>
    /// <param name="moveInUci"> The move to be played in UCI notation</param>
    /// <returns> The board position after the move provided is played.</returns>
    public void PlayMove(string moveInUci)
    {
        // the 'a' file would be 0, b = 1, etc.
        int startFile   = moveInUci[0].CompareTo('a'); 
        int endFile     = moveInUci[2].CompareTo('a');
        // First rank in the array will be the eighth. Zero-based so we subtract by 1 
        int startRank   = ReverseRankValue((int)Char.GetNumericValue(moveInUci[1])) - 1;
        int endRank     = ReverseRankValue((int)Char.GetNumericValue(moveInUci[3])) - 1;

        char[,] fenArr  = FenStringToArray(); // 8x8 chess board with piece/pawn
        char pieceBeingMoved;

        // Special case 1: pawn promotes, extra notation in the end. e.g. e7e8q
        if (moveInUci.Length == 5)
            pieceBeingMoved = moveInUci[4];
        else
            pieceBeingMoved = fenArr[startRank, startFile];

        // Special case 2: En passant, check if pawn attacked diagonally to an empty square
        if ((pieceBeingMoved == 'p' || pieceBeingMoved == 'P') &&
            (endFile == startFile - 1 || endFile == startFile + 1) &&
            fenArr[endRank, endFile] == '-')
        {
            fenArr[startRank, endFile] = '-'; // en passant capture
        }

        // move the piece/pawn and leave an empty square in its previous position.
        fenArr[startRank, startFile] = '-';
        fenArr[endRank, endFile] = pieceBeingMoved;

        // Special case 3: King castles, move rook as well,
        if (moveInUci.Equals("e1g1")) // white king-side castle
        {
            fenArr[7, 7] = '-';
            fenArr[7, 5] = 'R';
        }
        else if (moveInUci.Equals("e1c1")) // white queen-side castle
        {
            fenArr[7, 0] = '-';
            fenArr[7, 3] = 'R';
        }
        else if (moveInUci.Equals("e8g8")) // black king-side castle
        {
            fenArr[0, 7] = '-';
            fenArr[0, 5] = 'r';
        }
        else if (moveInUci.Equals("e8c8")) // black queen-side castle
        {
            fenArr[0, 7] = '-';
            fenArr[0, 3] = 'r';
        }
        DeleteMoveFromList(); // updates nextMoves
        ToggleCurrentPlayersTurn(); // switch to other side

        boardPosition = FenArrayToString(fenArr);
    }

    /// <summary>
    /// Saves the state of the puzzle so users can follow up with answers.
    /// </summary>
    /// <param name="channelId"> The channel the puzzle is being played in. </param>
    public void StorePuzzleState(string channelId)
    {
        AmazonDynamoDBClient ddbClient = new AmazonDynamoDBClient();
        string tableName = "ChessPuzzleStates";

        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = new Dictionary<string, AttributeValue>()
              {
                  { "ChannelId", new AttributeValue { S = channelId }},
                  { "FEN", new AttributeValue { S = boardPosition }},
                  { "Moves", new AttributeValue { S = nextMoves }},
                  { "PlayingAs", new AttributeValue { S = playingAs }}
              }
        };
        ddbClient.PutItemAsync(request);
    }

    /// <summary>
    /// Removes the puzzle from the saved-state database if there is an active puzzle in the provided channel.
    /// </summary>
    /// <param name="channelId"> The Discord channel to delete the puzzle from. </param>
    public static void DeletePuzzleState(string channelId)
    {
        AmazonDynamoDBClient ddbClient = new AmazonDynamoDBClient();
        string tableName = "ChessPuzzleStates";

        var request = new DeleteItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>() { { "ChannelId", new AttributeValue { S = channelId } } }
        };
        ddbClient.DeleteItemAsync(request);
    }

    /// <param name="channelId"> The Discord Channel to check. </param>
    /// <returns> True if there is an ongoing puzzle in the provided Discord channel </returns>
    public static async Task<bool> IsOngoingPuzzle(string channelId)
    {
        AmazonDynamoDBClient ddbClient = new AmazonDynamoDBClient();
        var checkChannelRequest = new QueryRequest
        {
            TableName = "ChessPuzzleStates",
            KeyConditionExpression = "ChannelId = :" + channelId,
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":" + channelId, new AttributeValue { S = channelId }}
                }
        };
        QueryResponse dbResponse = await ddbClient.QueryAsync(checkChannelRequest);

        return dbResponse.Count != 0;
    }

    /// <summary>
    /// Delete the next move from the list of moves required to complete this puzzle.
    /// </summary>
    private void DeleteMoveFromList()
    {
        int moveEndIndex = nextMoves.IndexOf(' ');
        if (moveEndIndex != -1)
            nextMoves = nextMoves.Substring(moveEndIndex + 1);
        else
            nextMoves = ""; // out of moves for this puzzle. Puzzle completed.
    }

    /// <param name="fenArr"> A 0-based array representation of an 8x8 chess board </param>
    /// <returns> The board position as a FEN string </returns>
    private static string FenArrayToString(char [,] fenArr)
    {
        string fen = "";
        for (int i = 0; i < fenArr.GetLength(0); i++)
        {
            int fenDigit = 0;
            for (int j = 0; j < fenArr.GetLength(1); j++)
            {
                if (fenArr[i, j] == '-')
                    fenDigit++;
                else if (fenDigit != 0 & fenArr[i, j] != '-')
                {
                    fen += fenDigit;
                    fen += fenArr[i, j];
                    fenDigit = 0;
                }
                else
                    fen += fenArr[i, j];
            }
            if (fenDigit != 0) // for when rank ends with an empty space/digit
                fen += fenDigit;
            fen += '/';
        }
        fen = fen.Remove(fen.Length - 1); // remove last slash
        return fen;
    }

    /// <returns> A 0-based array representation of an 8x8 chess board </returns>
    private char[,] FenStringToArray()
    {
        char[,] fenArr = new char[8, 8];
        for (int i = 0; i < fenArr.GetLength(0); i++) // rank
        {
            string currFenRank = boardPosition.Split('/')[i];

            for (int j = 0; j < fenArr.GetLength(1); j++) // file
            {
                char currChar = GetFenCharForFile(currFenRank, j + 1);
                if (currChar == '/')
                    break;              
                if (Char.IsDigit(currChar))
                    fenArr[i, j] = '-';
                if (Char.IsLetter(currChar))
                    fenArr[i, j] = currChar;   
            }
        }
        return fenArr;
    }

    /// <param name="fenRank"> A rank from the FEN board position </param>
    /// <param name="file"> The file value </param>
    /// <returns> The digit or piece/pawn letter in the fen rank 
    /// string that corresponds to this file. Returns 0 for failure.
    /// </returns>
    private static char GetFenCharForFile(string fenRank, int file)
    {
        int positionCount = 0;
        for (int i = 0; i < fenRank.Length; i++)
        {
            if (Char.IsDigit(fenRank[i]))
                positionCount += (int) Char.GetNumericValue(fenRank[i]);
            else
                positionCount++;
            if (positionCount >= file)
                return fenRank[i];
        }
        return '0';
    }

    /// <returns> A list of puzzles from the partition key provided </returns>
    private async Task<QueryResponse> QueryPuzzleTable(int partitionKey)
    {
        AmazonDynamoDBClient ddbClient = new AmazonDynamoDBClient();

        var queryRequest = new QueryRequest
        {
            TableName = "ChessPuzzles",
            KeyConditionExpression = "PartitionKey = :" + partitionKey,
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                {":" + partitionKey, new AttributeValue { N = partitionKey.ToString() }}
            }
        };
        QueryResponse dbResponse = await ddbClient.QueryAsync(queryRequest);

        return dbResponse;
    }

    /// <summary>
    /// FEN is written in reverse starting with the eighth rank. Each rank is delimited by a '/'.
    /// </summary>
    /// <param name="rank"> A chess rank from 1 to 8 </param>
    /// <returns> Returns the correct rank index in a FEN position. Reversed rank value. </returns>
    private static int ReverseRankValue(int rank)
    {
        return (8 - rank) + 1;
    }

    private void ToggleCurrentPlayersTurn()
    {
        whitesTurn = !whitesTurn;
    }

    /// <param name="rating"> Rating to base the random key selection around </param>
    /// <returns> Retrieves a random partition key around the rating preference provided.</returns>
    private static int GetRandomPartitionKey(int rating)
    {
        if (rating < 700)
            return rand.Next(1, 4560);
        else if (rating < 1000)
            return rand.Next(4560, 19270);
        else if (rating < 1200)
            return rand.Next(19270, 32531);
        else if (rating < 1400)
            return rand.Next(32531, 46496);
        else if (rating < 1600)
            return rand.Next(46496, 61703);
        else if (rating < 1800)
            return rand.Next(61703, 72661);
        else if (rating < 2000)
            return rand.Next(72661, 81832);
        else if (rating < 2200)
            return rand.Next(81832, 89102);
        else if (rating < 2400)
            return rand.Next(89102, 93563);
        else if (rating < 2600)
            return rand.Next(93563, 96053);
        else
            return rand.Next(96053, 96814);
    }
}
