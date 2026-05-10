using UnityEngine;
using UnityEngine.SceneManagement; // Necesario para cambiar de escenas

public class MenuManager : MonoBehaviour
{
    // Función para los botones azules
    public void CargarEscena(string nombreDeLaEscena)
    {
        SceneManager.LoadScene(nombreDeLaEscena);
    }

    // Función para el botón rojo (Salir)
    public void CerrarJuego()
    {
        // Esto mostrará un mensaje en la consola de Unity para que sepas que funciona
        Debug.Log("Saliendo del juego...");

        // Esto cerrará el juego real una vez que lo exportes (.exe, .apk, etc.)
        Application.Quit();
    }
}