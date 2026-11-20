using UnityEditor;
using UnityEngine;

namespace AuctionGame.Auction.Editor
{
    [CustomEditor(typeof(AuctionManager))]
    internal sealed class AuctionManagerEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.IsPlaying(target);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.IsPlaying(target))
                return;

            var auction = (AuctionManager)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Budgets and Purchases", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Player", "$" + auction.Economy.AvailableBudget);
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (PurchasedItem purchase in auction.Economy.Purchases)
                    DrawPurchase(purchase.item, purchase.purchasePrice);
            }

            foreach (AIBidder opponent in auction.Opponents)
            {
                EditorGUILayout.LabelField(opponent.Personality.Name, "$" + opponent.AvailableBudget);
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (AIPurchase purchase in opponent.Purchases)
                        DrawPurchase(purchase.Item, purchase.PurchasePrice);
                }
            }

            if (auction.Opponents.Count == 0)
                EditorGUILayout.HelpBox("Opponent budgets appear when the auction starts.", MessageType.Info);
        }

        private static void DrawPurchase(AuctionItem item, int price) =>
            EditorGUILayout.LabelField(item.ItemName, "$" + price);
    }
}
