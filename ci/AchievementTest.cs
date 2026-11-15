using UnityEngine;

public class AchievementTest : MonoBehaviour
{
	public shopManager shopManager;
	public int forageTaken;
	public fishingSite fishingSite;
	public int harvested;
	public moneyCount moneyCount;
	public int totalMoneyEarned;
	public playerController playerController;
	public GameObject checkFishTrigger;

	public void achievement1check() { }
	public void achievement2check()
	{
		if (fishingSite.smallCatched && fishingSite.midCatched && fishingSite.bigCatched && fishingSite.trashCatched)
		{
			checkFishTrigger.SetActive(false);
		}
	}
	public void achievement3check() { forageTaken++; }
	public void achievement4check() { harvested++; }
	public void achievement5check() { }
	public void achievement6check() { }
	public void achievement7check() { }
	public void achievement8check() { }
	public void achievement9check() { }
	public void achievement10check() { }
	public void achievement11check() { }
	public void achievement12check() { }
	public void CheckAllAchievements() { }
	public void UnlockAchievement(string id) { }
	public void ResetAchievement(string id) { }
}
