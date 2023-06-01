using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Utils
{
    // flatten a 2d array into a 1d array
    public static T[] Flatten2D<T>(T[][] array)
    {
        int rows = array.Length;
        int cols = array[0].Length;
        T[] flat = new T[rows * cols];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                flat[col + row * cols] = array[row][col];
            }
        }
        return flat;
    }

    // convert two arrays into a dictionary
    public static Dictionary<T1, T2> Zip<T1, T2>(T1[] a1, T2[] a2)
    {
        Dictionary<T1, T2> d = new Dictionary<T1, T2>();
        int l = Mathf.Min(a1.Length, a2.Length);
        for (int i = 0; i < l; i++)
        {
            d[a1[i]] = a2[i];
        }
        return d;
    }

    // convert all members of one array to a new list based on key-value pairs in a dictionary
    public static List<T2> Decode<T1, T2>(T1[] encoded, Dictionary<T1,T2> cipher)
    {
        List<T2> decoded = new List<T2>();
        foreach(T1 code in encoded)
        {
            decoded.Add(cipher[code]);
        }
        return decoded;
    }

    public static string MoneyToString(float money)
    {
        return string.Format("{0:N2}", money);
    }
}
