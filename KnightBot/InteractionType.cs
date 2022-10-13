using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightBot;

/// <summary>
/// Types of interactions that can be received from Discord API.
/// </summary>
internal enum InteractionType
{
    // A ping
    Ping = 1,

    // A command invocation.
    ApplicationCommand,

    // Usage of a message's component.
    MessageComponent,

    // An interaction sent when an application command option is filled out.
    ApplicationCommandAutocomplete,

    // An interaction sent when a modal is submitted.
    ModalSubmit
}
