# Knight Bot
Play chess puzzles with friends from your Discord chat room at any difficulty rating. No registration needed.

```
/puzzle [rating] 
/answer <move in UCI notation>
```

<img src="https://github.com/OFNoah/KnightBot/blob/main/readme_assets/puzzleDemo.gif" width="35%" height="55%">

## Context
A personal project for me to brush up on various Amazon Web Services, databases, and .NET/C#. 

Discord, a popular chat platform, has been moving its API towards
['Privileged Intent'](https://support-dev.discord.com/hc/en-us/articles/4404772028055), which stops user-developed bots from listening to all chat messages.
Instead, bots are limited to listening for commands targeted specifically towards them. This was an opportunity for me to create a bot that __only runs when a
command is called__ to trigger it by utilizing AWS Lambda Functions and API Gateway. 

This repository contains code for command registration with Discord's API, as well as the code that runs when the commands are called on a 
Discord channel that contains the bot.

## AWS and Project Log
### Project Development Description
All services used in this project fall under Amazon's free-tier except for the import of the Lichess puzzle data set to DynamoDB via S3, costing about $0.08. 
DynamoDB is also used to store the state of puzzles as they are being solved by users. To efficiently return a random puzzle at the user's chosen difficulty
rating (between 500 and 2800), the table was sorted by rating and provided partition & sort keys. The table can run off of 1 Read Capacity Unit to find
a suitable puzzle, and takes around a second to retrieve. This is the minimum reading time with current DynamoDB capabilities.

Discord is provided with an API Gateway endpoint to direct command calls to, and the Gateway is configured to call the ``handleDiscordRequestChess``
Lambda function on such event. After signature verification, a response is prepared based on the information provided and the function either reads
a puzzle and returns it, or checks a move provided by users. Sensitive text such as bot tokens and IDs are stored as encrypted environment variables.

### Service Architecture

<img src="https://github.com/OFNoah/KnightBot/blob/main/readme_assets/applicationDesign.jpg" width="75%" height="75%">

### Retrospective and Takeaways
- 80% of the application's runtime is spent on startup (on a cold start). From my reading, .NET 6 has a much longer startup time than if I would
have used Node.js or Python. A long running application that spans minutes would likely work better for .NET 6.

- I wrote my own helper functions instead of using a Discord library to practice reading API and interacting in a more direct way with it.
Manipulating JSON was not as easy as it would have been with Python or JavaScript, and I would surely use a library for a larger
project.

- The Lambda function itself could have been split into two Lambdas to handle ``/puzzle`` and ``/answer`` commands separately, but they share many of the
same helper functions and libraries so they would not be cut down by much.

- It would be best to have a CloudFormation template that can quickly redeploy all resources to easily replicate this project. That being said, deploying the chess puzzle table could be a challenge, as I had to tailor it manually for this project locally on Microsoft SQL Server. Perhaps the tailored CSV can be made public on my S3 bucket for it to be importable by others. This is a topic I will be exploring next.

- The puzzle data set shows the board position one move before the puzzle actually begins. This is important as the opponent's last move
could influence your next. However, this meant that I had to have code in place that can change the chess board position for any move played.

## Libraries and Tools Used
- [Lichess Puzzle Dataset](https://database.lichess.org/#puzzles) A great non-profit that provides free chess online play, game analysis, as well as extensive
data sets. The table used in this project consists of ~3 million puzzles, generated over 35 years of CPU time!
- [AWSSDK.DynamoDBv2](https://github.com/aws/aws-sdk-net)
- [Amazon.Lambda.Core](https://github.com/aws/aws-lambda-dotnet/tree/master/Libraries/src/Amazon.Lambda.Core)
- [Amazon.Lambda.APIGatewayEvents](https://github.com/aws/aws-lambda-dotnet/tree/master/Libraries/src/Amazon.Lambda.APIGatewayEvents)
- [NSec.Cryptography](https://nsec.rocks/) For signature verification when a Discord command is received.
- [Chessvision.ai](https://chessvision.ai/docs/tools/fen2image/) For visualization of the chessboard as an image.
