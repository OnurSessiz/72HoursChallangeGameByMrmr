using UnityEditor;
using UnityEngine;

/// <summary>
/// Hasar sistemini elle bileşen eklemeden kurmak için küçük menü yardımcıları.
/// Menü: Tools/Combat/...
/// </summary>
public static class CombatSetup
{
    [MenuItem("Tools/Combat/Setup Player Attack")]
    public static void SetupPlayerAttack()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Player bulunamadı",
                "Sahnede 'Player' tag'li bir obje yok. Oyuncuya Player tag'ini verip tekrar dene.", "Tamam");
            return;
        }

        if (player.GetComponent<PlayerAttack>() != null)
        {
            Selection.activeGameObject = player;
            Debug.Log("[CombatSetup] PlayerAttack zaten ekli: " + player.name, player);
            return;
        }

        Undo.AddComponent<PlayerAttack>(player);
        Selection.activeGameObject = player;
        Debug.Log("[CombatSetup] PlayerAttack eklendi: " + player.name, player);
    }

    [MenuItem("Tools/Combat/Add Health To Selection")]
    public static void AddHealthToSelection()
    {
        int added = 0;

        foreach (var go in Selection.gameObjects)
        {
            if (go.GetComponent<Health>() != null) continue;
            Undo.AddComponent<Health>(go);
            added++;
        }

        Debug.Log("[CombatSetup] " + added + " objeye Health eklendi.");
    }

    [MenuItem("Tools/Combat/Add Health To Selection", true)]
    private static bool AddHealthToSelectionValidate() => Selection.gameObjects.Length > 0;
}
