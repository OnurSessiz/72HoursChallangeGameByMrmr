/// <summary>
/// Her mekaniğin izole bir state olduğu state-machine arayüzü.
/// Enter/Exit geçişte bir kez; Tick her Update, FixedTick her FixedUpdate çağrılır.
/// </summary>
public interface IPlayerState
{
    void Enter();
    void Tick();       // Update: input tepkileri, timer kontrolleri
    void FixedTick();  // FixedUpdate: fizik (hareket, rotasyon)
    void Exit();
}
