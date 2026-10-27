using System;
using System.IO;
using Kairos.Engine;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class UiTemplateManifestTests
    {
        [Test] public void EveryDeclaredUiTemplateExistsAndHasUniquePath()
        {
            string root = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(root, "docs/data/cards.json"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Missing root");
            string directory = Path.Combine(root, "Assets/Kairos/UI/Resources/KairosUI");
            foreach (var name in new[] { "Home", "Collection", "Battle", "Campaign", "DeckPicker", "WorkshopSlots", "KeeperPicker", "Profile", "Receipts", "RemoveCard", "LoadFailure", "PrivacyCurtain" })
                Assert.IsTrue(File.Exists(Path.Combine(directory, "Pages", name + ".prefab")), name);
            foreach (var name in new[] { "Abandon", "CardDetails", "Choice", "Craft", "DeleteDeck", "Leave", "Result", "Rules", "SaveError", "TutorialComplete", "TutorialIntro", "TutorialLeave", "TutorialWelcome", "UnsavedWorkshop" })
                Assert.IsTrue(File.Exists(Path.Combine(directory, "Modals", name + ".prefab")), name);
            foreach (var name in new[] { "Header", "Workshop", "CollectionFilters", "CollectionGrid", "CollectionFooter", "Timeline", "ActionWindow", "BattleSidebar", "TutorialSidebar", "Hand", "Board0", "Board1", "PlayerInfo0", "PlayerInfo1" })
                Assert.IsTrue(File.Exists(Path.Combine(directory, "Panels", name + ".prefab")), name);
            Assert.IsTrue(File.Exists(Path.Combine(directory, "UIRoot.prefab")));
        }
    }
}
