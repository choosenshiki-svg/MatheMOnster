using UnityEngine;

public class RandomBallSummoner : MonoBehaviour
{
    private const int BallCount = 5;
    private const float BallSpacing = 2.1f;

    private static readonly int[] SmallNumberWeights =
    {
        1, 8, 7, 6, 5, 4, 3, 2, 1
    };

    private static readonly string[] CommonVariables =
    {
        "x", "x", "y", "y", "a", "b", "c", "n"
    };

    private static readonly string[] Functions =
    {
        "root", "log", "sin", "cos", "tan", "asin", "acos", "atan"
    };

    private Spawner spawner;

    private void Awake()
    {
        spawner = GetComponent<Spawner>();
        if (spawner == null)
        {
            Debug.LogError("RandomBallSummoner requires a Spawner on the same object.", this);
        }
    }

    public void SpawnRandomBallsAt(Vector3 center)
    {
        if (spawner == null)
        {
            return;
        }

        for (int index = 0; index < BallCount; index++)
        {
            float horizontalOffset = (index - (BallCount - 1) / 2f) * BallSpacing;
            Vector3 position = center + Vector3.right * horizontalOffset;
            spawner.SpawnBallWithContent(position, ChooseMathToken());
        }
    }

    private static string ChooseMathToken()
    {
        int choice = Random.Range(0, 100);
        if (choice < 65)
        {
            return ChooseNumber().ToString();
        }

        if (choice < 86)
        {
            return ChooseCommonOperator();
        }

        if (choice < 92)
        {
            return "=";
        }

        if (choice < 98)
        {
            return CommonVariables[Random.Range(0, CommonVariables.Length)];
        }

        return ChooseAdvancedToken();
    }

    private static int ChooseNumber()
    {
        int size = Random.Range(0, 1000);
        int number;
        if (size < 850)
        {
            number = ChooseSmallNumber();
        }
        else if (size < 995)
        {
            number = Random.Range(10, 101);
        }
        else
        {
            number = Random.Range(101, 1001);
        }

        return Random.Range(0, 100) < 10 ? -number : number;
    }

    private static int ChooseSmallNumber()
    {
        int totalWeight = 0;
        foreach (int weight in SmallNumberWeights)
        {
            totalWeight += weight;
        }

        int roll = Random.Range(0, totalWeight);
        for (int index = 0; index < SmallNumberWeights.Length; index++)
        {
            if (roll < SmallNumberWeights[index])
            {
                return index + 1;
            }

            roll -= SmallNumberWeights[index];
        }

        return SmallNumberWeights.Length;
    }

    private static string ChooseCommonOperator()
    {
        int choice = Random.Range(0, 100);
        if (choice < 35)
        {
            return "+";
        }

        if (choice < 60)
        {
            return "-";
        }

        if (choice < 80)
        {
            return "*";
        }

        return "/";
    }

    private static string ChooseAdvancedToken()
    {
        int choice = Random.Range(0, 100);
        if (choice < 30)
        {
            return "^";
        }

        if (choice < 45)
        {
            return "\u00d7";
        }

        if (choice < 60)
        {
            return "\u00f7";
        }

        if (choice < 75)
        {
            return "!";
        }

        if (choice < 90)
        {
            return "%";
        }

        return Functions[Random.Range(0, Functions.Length)];
    }
}
