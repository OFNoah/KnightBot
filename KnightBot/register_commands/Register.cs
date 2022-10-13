using System.Net;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using NSec.Cryptography;
using System.Text;
using Amazon.Lambda.Serialization.SystemTextJson;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.Net.Http.Headers;

namespace KnightBot.register_commands;

internal class Register
{
    private string tokenHeader = "Bot " + Environment.GetEnvironmentVariable("BOT_TOKEN");
    private string applicationId = Environment.GetEnvironmentVariable("APPLICATION_ID");
    private string url;
    private static readonly HttpClient client = new HttpClient();

    public Register()
    {
        this.url = "https://discord.com/api/v10/applications/" + applicationId + "/commands";
        client.DefaultRequestHeaders.Add("Authorization", tokenHeader);
    }

    public async Task<APIGatewayProxyResponse> RegisterCommands()
    {
        await RegisterPuzzleCommand();
        await RegisterAnswerCommand();

        Console.WriteLine("Command registration.");
        return new APIGatewayProxyResponse { };
    }

    public async Task<string> RegisterPuzzleCommand()
    {
        Dictionary<string, object> responsDict = new Dictionary<string, object>
        {
            {"name", "puzzle"},
            {"type", 1},
            {"description", "Creates a chess puzzle."},
            {"options", new List<Dictionary<string,object>>
                { new Dictionary<string, object>
                {
                    {"name", "rating"},
                    {"description", "Elo rating difficulty of the puzzle (500 to 2800)"},
                    {"type", 4}
                } }
            }
        };

        string response = JsonSerializer.Serialize(responsDict);

        StringContent stringContent = new (response, Encoding.UTF8, "application/json");

        var discordResponse = await client.PostAsync(url, stringContent);
        string bodyresponse = await discordResponse.Content.ReadAsStringAsync();
        return bodyresponse;
    }

    public async Task<string> RegisterAnswerCommand()
    {
        Dictionary<string, object> responsDict = new Dictionary<string, object>
        {
            {"name", "answer"},
            {"type", 1},
            {"description", "The best move in UCI notation to answer the current puzzle."},
            {"options", new List<Dictionary<string,object>>
                { new Dictionary<string, object>
                {
                    {"name", "move"},
                    {"description", "E.g. Bishop currently on f8 moves to d6 = d8f6. " +
                    "Promotion piece on the end e.g. e2e1q"},
                    {"type", 3},
                    {"required", true}
                } }
            }
        };

        string response = JsonSerializer.Serialize(responsDict);

        StringContent stringContent = new(response, Encoding.UTF8, "application/json");

        var discordResponse = await client.PostAsync(url, stringContent);
        string bodyresponse = await discordResponse.Content.ReadAsStringAsync();
        return bodyresponse;
    }
}

