using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightBot;

/// <summary>
/// Types of interactions that can be sent to Discord API.
/// </summary>
internal enum InteractionResponseType
{
    // Acknowledgement for a ping
    Pong = 1,

    // Respond with a message, showing the user's input.
    ChannelMessageWithSource = 4,

    // Acknowledge a command without sending a message, showing the user's input.
    // Requires follow-up.
    DeferredChannelMessageWithSource = 5,

    // Acknowledge an interaction and edit the original message that contains
    // the component later. The user does not see a loading state.
    DeferredUpdateMessage = 6,

    // Edit the message the component was attached to.
    UpdateMessage = 7,

    // Callback for an app to define the results to the user.
    ApplicationCommandAutocompleteResult = 8,

    // Respond with a modal.
    Modal = 9
}
