using UnityEngine;

namespace SmallPot
{
    /// <summary>
    /// Marks a widget that was already in the scene when the interface was built, so the interface leaves
    /// it alone from then on.
    ///
    /// The interface is authored once and after that it belongs to whoever opens the scene. A later build
    /// finds the widget, marks it, and refreshes only what comes from the data - the words, the sprites,
    /// the states - while the layout and the component parameters stay exactly as they were found. Without
    /// this, every size, font and colour set by hand is written over a frame after the game starts, which
    /// makes the scene impossible to finish.
    ///
    /// It carries no data: its presence is the whole message.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuthoredWidget : MonoBehaviour
    {
    }
}
