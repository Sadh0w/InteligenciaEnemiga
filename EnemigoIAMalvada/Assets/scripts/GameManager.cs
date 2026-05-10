using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Configuración de Victoria")]
    public string nombreDeLaCapa = "Enemigo";
    public GameObject canvasVictoria;        // El que APARECE
    public GameObject interfazJuego;        // El que DESAPARECE (Botones, etc)
    public string nombreEscenaMenu = "MenuPrincipal";

    private bool juegoTerminado = false;

    void Start()
    {
        if (canvasVictoria != null) canvasVictoria.SetActive(false);
        if (interfazJuego != null) interfazJuego.SetActive(true);
    }

    void Update()
    {
        if (juegoTerminado) return;

        if (ContarObjetosConCapa(nombreDeLaCapa) <= 0)
        {
            GanarPartida();
        }
    }

    int ContarObjetosConCapa(string capa)
    {
        int capaIndex = LayerMask.NameToLayer(capa);
        GameObject[] todosLosObjetos = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int contador = 0;

        foreach (GameObject obj in todosLosObjetos)
        {
            if (obj.layer == capaIndex) contador++;
        }
        return contador;
    }

    void GanarPartida()
    {
        juegoTerminado = true;

        // Ocultamos la interfaz normal del juego
        if (interfazJuego != null)
            interfazJuego.SetActive(false);

        // Mostramos el cartel de victoria
        if (canvasVictoria != null)
            canvasVictoria.SetActive(true);
    }

    public void IrAlMenu()
    {
        SceneManager.LoadScene(nombreEscenaMenu);
    }
}