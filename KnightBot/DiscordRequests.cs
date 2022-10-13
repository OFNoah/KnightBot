using Amazon.Lambda.Core;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NSec.Cryptography;
using PublicKey = NSec.Cryptography.PublicKey;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace KnightBot;

public class DiscordRequests
{
    private static readonly HttpClient client = new HttpClient();
    private const int BrownEmbed = 0x9f510d;
    private const int RedEmbed = 0xff1500;
    private const int GreenEmbed = 0x68d608;

    /// <summary>
    /// Handles all Discord slash commands requested to Knight Bot.
    /// </summary>
    public async Task<APIGatewayProxyResponse> DiscordRequestHandler(APIGatewayProxyRequest request,
                                                ILambdaContext context)
    {
        string rawBody = request.Body;
        JsonNode bodyInJson = MakeJsonObject(rawBody);

        context.Logger.LogInformation("Request received: " + rawBody);

        // Verifying the received request as per Discord requirements.
        bool verified = VerifySignature(request);
        if (!verified)
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.Unauthorized
            };

        // Acknowledging ping messages as per Discord requirements.
        if (bodyInJson["type"] is not null &&
            bodyInJson["type"].GetValue<int>() == (int)InteractionType.Ping)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonSerializer.Serialize(new Dictionary<string, InteractionResponseType>
                {
                    {"type", InteractionResponseType.Pong}
                })
            };
        }

        // Generate a new puzzle if user is not engaged in any in that channel.
        if (bodyInJson["type"] is not null &&
            bodyInJson["data"]["name"] is not null &&
            bodyInJson["type"].GetValue<int>() == (int)InteractionType.ApplicationCommand &&
            bodyInJson["data"]["name"].GetValue<string>().Equals("puzzle"))
        {
            string interactionId = bodyInJson["id"].GetValue<string>();
            string interactionToken = bodyInJson["token"].GetValue<string>();
            string applicationID = bodyInJson["application_id"].GetValue<string>();
            string channelId = bodyInJson["channel_id"].GetValue<string>();
            int rating;
            // check if rating was provided by user, otherwise provide default rating.
            try { rating = bodyInJson["data"]["options"][0]["value"].GetValue<int>(); }
            catch { rating = 1500; }

            await DeferMessage(interactionId, interactionToken);

            if (await Puzzle.IsOngoingPuzzle(channelId))
            {
                StringContent followupMsgPuzzleInProgress =
                    FollowupMessageWriter("A puzzle is already in progress in this channel!",
                                          BrownEmbed);

                await client.PatchAsync("https://discord.com/api/v10/webhooks/" +
                applicationID + "/" + interactionToken + "/messages/@original",
                followupMsgPuzzleInProgress);

                return new APIGatewayProxyResponse { };
            }

            Puzzle puzzle = new Puzzle(rating);
            string opponentMove = puzzle.nextMoves.Split(" ")[0];
            puzzle.PlayMove(opponentMove);

            string url = "https://fen2image.chessvision.ai/" + puzzle.boardPosition +
                "?turn=" + puzzle.playingAs + "&pov=" + puzzle.playingAs;
            string titleText = "Rating: " + puzzle.puzzleRating + " -- Last move: " + opponentMove;

            StringContent followupMsg = FollowupMessageWriter(titleText,
                                                              BrownEmbed,
                                                              url,
                                                              "From the Lichess Puzzle Dataset");

            await client.PatchAsync("https://discord.com/api/v10/webhooks/" +
                applicationID + "/" + interactionToken + "/messages/@original", followupMsg);

            puzzle.StorePuzzleState(channelId);

            // empty return as followup response already sent
            return new APIGatewayProxyResponse { };
        }

        // Answers the next move in a puzzle if a puzzle is ongoing in the channel.
        if (bodyInJson["type"] is not null &&
            bodyInJson["data"]["name"] is not null &&
            bodyInJson["type"].GetValue<int>() == (int)InteractionType.ApplicationCommand &&
            bodyInJson["data"]["name"].GetValue<string>().Equals("answer"))
        {
            string interactionId = bodyInJson["id"].GetValue<string>();
            string interactionToken = bodyInJson["token"].GetValue<string>();
            string applicationID = bodyInJson["application_id"].GetValue<string>();
            string channelId = bodyInJson["channel_id"].GetValue<string>();

            await DeferMessage(interactionId, interactionToken);
            StringContent followupMsgPuzzleInProgress;

            if (!await Puzzle.IsOngoingPuzzle(channelId))
            {
                followupMsgPuzzleInProgress =
                    FollowupMessageWriter("There is no puzzle in progress in this channel!" +
                    " Use /puzzle to start one.", BrownEmbed);

                await client.PatchAsync("https://discord.com/api/v10/webhooks/" +
                applicationID + "/" + interactionToken + "/messages/@original", followupMsgPuzzleInProgress);

                return new APIGatewayProxyResponse { };
            }

            Puzzle puzzleInProgress = await Puzzle.RetrievePuzzle(channelId);
            string playerMove = bodyInJson["data"]["options"][0]["value"].GetValue<string>();
            string moveToBePlayed = puzzleInProgress.nextMoves.Split(" ")[0];


            if (!playerMove.Equals(moveToBePlayed)) // incorrect move by player
            {
                followupMsgPuzzleInProgress =
                    FollowupMessageWriter(":x: Move made: " + playerMove, RedEmbed);

                await client.PatchAsync("https://discord.com/api/v10/webhooks/" +
                    applicationID + "/" + interactionToken + "/messages/@original",
                    followupMsgPuzzleInProgress);
                return new APIGatewayProxyResponse { };
            }

            puzzleInProgress.PlayMove(playerMove);

            if (!String.IsNullOrEmpty(puzzleInProgress.nextMoves)) // more moves to be made, play 'opponent' move
            {
                string opponentMove = puzzleInProgress.nextMoves.Split(" ")[0];
                followupMsgPuzzleInProgress =
                    FollowupMessageWriter(":white_check_mark: Move made: " + playerMove +
                    "\nOpponent played: " + opponentMove, GreenEmbed);

                puzzleInProgress.PlayMove(opponentMove);
                puzzleInProgress.StorePuzzleState(channelId);
            }
            else // Puzzle is complete
            {
                followupMsgPuzzleInProgress =
                    FollowupMessageWriter(":white_check_mark: Move made: " + playerMove +
                    "\nPuzzle completed!  :confetti_ball:", GreenEmbed);

                Puzzle.DeletePuzzleState(channelId);
            }

            await client.PatchAsync("https://discord.com/api/v10/webhooks/" +
                applicationID + "/" + interactionToken + "/messages/@original",
                followupMsgPuzzleInProgress);

            return new APIGatewayProxyResponse { };
        }

        // No matches. Return bad request (400).
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)HttpStatusCode.BadRequest
        };
    }


    /// <summary>
    /// Used as an initial response to Discord commands that will take more than 3s.
    /// </summary>
    private static async Task<string> DeferMessage(string interactionId, string interactionToken)
    {
        Dictionary<string, object> response = new Dictionary<string, object>
        {
            {"type", (int) InteractionResponseType.DeferredChannelMessageWithSource}
        };
        StringContent stringContent = new(JsonSerializer.Serialize(response),
                                           Encoding.UTF8,
                                           "application/json");
        var apiResponse = await client.PostAsync("https://discord.com/api/v10/interactions/" +
            interactionId + "/" + interactionToken + "/callback", stringContent);

        string responseBody = await apiResponse.Content.ReadAsStringAsync();

        return responseBody;
    }

    /// <summary>
    /// Provides a Discord template for an embedded followup message.
    /// </summary>
    private StringContent FollowupMessageWriter(string titleText, int embedColor, string imgUrl = "", string footer = "")
    {
        Dictionary<string, object> discordEmbedDict = new Dictionary<string, object>
        {
            {"embeds", new List<Dictionary<string,object>>
                { new Dictionary<string, object>
                    {
                        {"type", "rich"},
                        {"title", titleText},
                        {"image", new Dictionary<string, object>
                            {
                                {"url", imgUrl},
                                {"height", 0},
                                {"width", 0}
                            }
                        },
                        {"color", embedColor},
                        {"footer", new Dictionary<string, object>
                            {
                                {"text", footer}
                            }
                        }
                    }
                }
            }
        };
        StringContent stringContent = new(JsonSerializer.Serialize(discordEmbedDict),
                                          Encoding.UTF8,
                                          "application/json");
        return stringContent;
    }

    /// <summary>
    /// Verifies requests using Ed25519 signature scheme as per Discord requirements.
    /// </summary>
    /// <param name="request"> The request received from Discord through API Gateway Proxy </param>
    /// <returns> True for valid request </returns>
    private bool VerifySignature(APIGatewayProxyRequest request)
    {
        string signature = request.Headers["x-signature-ed25519"];
        string timestamp = request.Headers["x-signature-timestamp"];
        string key = Environment.GetEnvironmentVariable("PUBLIC_KEY");

        SignatureAlgorithm algorithm = SignatureAlgorithm.Ed25519;
        PublicKey publicKey = PublicKey.Import(algorithm,
                                               GetBytesFromHex(key),
                                               KeyBlobFormat.RawPublicKey);
        byte[] data = Encoding.UTF8.GetBytes(timestamp + request.Body);

        return algorithm.Verify(publicKey, data, GetBytesFromHex(signature));
    }

    /// <summary>
    /// Creates a JSON object whose properties can be accessed
    /// </summary>
    /// <param name="jsonCandidate"> A JSON as a string </param>
    /// <returns> A JSON object </returns>
    private JsonNode MakeJsonObject(string jsonCandidate)
    {
        byte[] bodyByteArray = Encoding.UTF8.GetBytes(jsonCandidate);
        MemoryStream ms = new MemoryStream(bodyByteArray);
        JsonNode jsonObject = new DefaultLambdaJsonSerializer().Deserialize<JsonNode>(ms);
        ms.Close();

        return jsonObject;
    }

    /// <returns> A byte array representation of the hex string </returns>
    private byte[] GetBytesFromHex(string hex)
    {
        var length = hex.Length;
        var bytes = new byte[length / 2];

        for (int i = 0; i < length; i += 2)
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);

        return bytes;
    }

}
