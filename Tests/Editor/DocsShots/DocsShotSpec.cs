#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using UnityEngine.UIElements;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// One docs picture written down as code: the view it shows, its size, the arrange step that runs before every
    /// read, and the elements it marks. A DocsShots class captures it with <see cref="Take"/>; a normal test runs
    /// the same steps in a host window without reading the screen, so a broken setup or a wrong marker name shows
    /// up before anyone takes a picture.
    /// </summary>
    public sealed class DocsShotSpec
    {
        public string Page;
        public string Name;
        public VisualElement View;
        public float Width = DocsShot.InspectorWidth;
        public float Height = 900f;
        public Action<VisualElement> Arrange;
        public (string elementName, int number)[] Markers = new (string, int)[0];

        /// <summary>Destroys what the setup made besides the avatar and the inspector, such as a window instance. Runs once the shot is done, taken or not.</summary>
        public Action Release;

        public void Take()
        {
            try
            {
                DocsShot.Take(Page, Name, View, Width, Height, Arrange, Markers);
            }
            finally
            {
                Release?.Invoke();
            }
        }
    }
}
#endif
