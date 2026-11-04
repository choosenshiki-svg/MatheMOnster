using System;
using System.IO;
using UnityEngine;

[Serializable]
public struct BallState
{
    public Vector2 position;
    public float   rotationZ;
    public int     ballNumber;
    public char    ballVariable;
    public int     variablePower;
    public string  displayedKey;
    public byte    inputMode;
    public bool    isSolverOutput;
    public bool    isLocked;
    public uint    ownerClientId;
    public byte    zone;
    public bool    isBagBall;

    public const int MaxDisplayedKeyLength = 24;

    public static BallState FromBall(BallNumber ball, uint ownerClientId, byte zone, bool isBagBall)
    {
        return new BallState
        {
            position       = ball.transform.position,
            rotationZ      = ball.transform.eulerAngles.z,
            ballNumber     = ball.NumberValue,
            ballVariable   = ball.Variable,
            variablePower  = ball.VariablePower,
            displayedKey   = ball.GetRawDisplayedKey(),
            inputMode      = (byte)ball.InputMode,
            isSolverOutput = ball.IsSolverOutput,
            isLocked       = ball.IsLocked,
            ownerClientId  = ownerClientId,
            zone           = zone,
            isBagBall      = isBagBall,
        };
    }

    public void ApplyTo(BallNumber ball, bool authoritative)
    {
        ball.transform.position = position;
        bool isDirectionalOperator = inputMode == (byte)BallInputMode.DisplayKey &&
            !isSolverOutput &&
            DirectionalOperator.IsDirectionalOperatorText(
                string.IsNullOrEmpty(displayedKey) ? string.Empty : displayedKey.Trim());
        if (isDirectionalOperator)
        {
            float currentRotation = ball.transform.eulerAngles.z;
            if (Mathf.Abs(Mathf.DeltaAngle(currentRotation, rotationZ)) > 0.1f)
                MathEquationBuilder.MarkConnectionsChanged();
            ball.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
        }

        ball.SetInputModeRaw((BallInputMode)inputMode);
        ball.SetAlgebraContentRaw(ballNumber, ballVariable, variablePower);
        ball.SetRawDisplayedKey(displayedKey ?? string.Empty, isSolverOutput);
        ball.SetLockedRaw(isLocked);
        ball.RefreshDisplay();

        if (authoritative)
        {
            var rb = ball.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                if (DirectionalOperator.IsDirectionalOperator(ball))
                    rb.angularVelocity = 0f;
            }
        }
    }

    public void Write(BinaryWriter w)
    {
        w.Write(position.x);
        w.Write(position.y);
        w.Write(rotationZ);
        w.Write(ballNumber);
        w.Write((ushort)ballVariable);
        w.Write(variablePower);
        w.Write(inputMode);
        w.Write(isSolverOutput);
        w.Write(isLocked);
        w.Write(ownerClientId);
        w.Write(zone);
        w.Write(isBagBall);

        string key = displayedKey ?? string.Empty;
        if (key.Length > MaxDisplayedKeyLength) key = key.Substring(0, MaxDisplayedKeyLength);
        w.Write((byte)key.Length);
        for (int i = 0; i < key.Length; i++) w.Write(key[i]);
    }

    public static BallState Read(BinaryReader r)
    {
        var s = new BallState
        {
            position       = new Vector2(r.ReadSingle(), r.ReadSingle()),
            rotationZ      = r.ReadSingle(),
            ballNumber     = r.ReadInt32(),
            ballVariable   = (char)r.ReadUInt16(),
            variablePower  = r.ReadInt32(),
            inputMode      = r.ReadByte(),
            isSolverOutput = r.ReadBoolean(),
            isLocked       = r.ReadBoolean(),
            ownerClientId  = r.ReadUInt32(),
            zone           = r.ReadByte(),
            isBagBall      = r.ReadBoolean(),
        };
        int len = r.ReadByte();
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = r.ReadChar();
        s.displayedKey = new string(chars);
        return s;
    }
}