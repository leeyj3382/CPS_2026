using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class UISystem : MonoBehaviour
{
    public SpawnSystem systemManager;
    public Canvas seedCanvas;
    public TMP_InputField seedInput;  
    public TextMeshProUGUI seedText;

    // Start is called before the first frame update
    void Start()
    {
        Time.timeScale = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void afterSeedInput()
    {
        // Seed 입력 후 SpawnSystem.Initialize 호출 — 내부에서 시드 셔플 + spawn 코루틴 시작.
        int parsedSeed;
        if (int.TryParse(seedInput.text, out parsedSeed))
        {
            systemManager.Initialize(parsedSeed);   // 내부에서 Random.InitState 후 셔플 + 코루틴 시작
            seedText.text = seedInput.text;
            seedCanvas.enabled = false;
            Time.timeScale = 1;
        }
        else
        {
            seedInput.text = "Seed must be integer";
        }
    }

    public void OnClickRestart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClickNormalSpeed()
    {
        Time.timeScale = 1;
    }
    public void OnClickDoubleSpeed()
    {
        Time.timeScale = 2;
    }
    public void OnClickTripleSpeed()
    {
        Time.timeScale = 3;
    }

}
