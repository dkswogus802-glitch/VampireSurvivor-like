using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PoolManager : MonoBehaviour
{
    //... 프리팹들을 보관할 변수 
    public GameObject[] prefabs;

    //... 풀 담당을 하는 리스트들
    List<GameObject>[] pools;

    void Awake()
    {
        pools = new List<GameObject>[prefabs.Length];

        for (int index = 0; index < pools.Length; index++)
        {
            pools[index] = new List<GameObject>();
        }
    }

    public GameObject Get(int index)
    {
        GameObject select = null;

        //... 선택한 풀의 놀고 있는 게임 오브젝트 접근

        foreach (GameObject item in pools[index])
        {
            if(!item.activeSelf)        //... 발견하면 select 변수에 할당 
            {
                select = item;
                select.SetActive(true);
                break;
            }
        }
        //... 못찾았으면?? 
        if (!select)
        { //... 새롭게 생성하고 select 함수에 할당
            select = Instantiate(prefabs[index], transform);
            pools[index].Add(select);
        }
        return select;
    }

    // [추가] 해당 풀에서 현재 활성화된(화면에 살아있는) 오브젝트 수
    public int CountActive(int index)
    {
        int count = 0;
        foreach (GameObject item in pools[index])
        {
            if (item.activeSelf)
                count++;
        }
        return count;
    }

}
