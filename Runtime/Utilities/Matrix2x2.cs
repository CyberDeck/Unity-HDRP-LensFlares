using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SecretLab.Utilities {
    // Incomplete helper class for matrix (2x2) caluclations

    [Serializable]
    public class Matrix2x2 {
        float[,] matrix = new float[2, 2];

        public Matrix2x2(float v00, float v01, float v10, float v11) {
            Set(v00, v01, v10, v11);
        }

        public Matrix2x2() {
            Set(1, 0, 0, 1);
        }

        public Matrix2x2(Matrix2x2 other) {
            Set(other[0, 0], other[0, 1], other[1, 0], other[1, 1]);
        }

        public float this[int row, int col] {
            get {
                return matrix[row, col];
            }
            set {
                matrix[row, col] = value;
            }
        }

        public void Set(float v1, float v2, float v3, float v4) {
            matrix[0, 0] = v1;
            matrix[0, 1] = v2;
            matrix[1, 0] = v3;
            matrix[1, 1] = v4;
        }

        public void SetRotation(float degree) {
            float rad = Mathf.Deg2Rad * degree;
            Set(Mathf.Cos(rad), -Mathf.Sin(rad), Mathf.Sin(rad), Mathf.Cos(rad));
        }

        public static Matrix2x2 Rotation(float degree) {
            Matrix2x2 result = new Matrix2x2();
            result.SetRotation(degree);
            result *= 0.5f;
            result += 0.5f;
            result = (result * 2.0f) - 1.0f;
            return result;
        }

        public static Matrix2x2 operator *(Matrix2x2 m, float scalar) {
            Matrix2x2 result = new Matrix2x2(m);
            for (int i = 0; i < 2; i++) {
                for (int j = 0; j < 2; j++) {
                    result[i, j] *= scalar;
                }
            }
            return result;
        }

        public static Matrix2x2 operator +(Matrix2x2 m, float scalar) {
            Matrix2x2 result = new Matrix2x2(m);
            for (int i = 0; i < 2; i++) {
                for (int j = 0; j < 2; j++) {
                    result[i, j] += scalar;
                }
            }
            return result;
        }

        public static Matrix2x2 operator -(Matrix2x2 m, float scalar) {
            return m + (-scalar);
        }

        public static Vector2 operator *(Matrix2x2 m, Vector2 v) {
            return new Vector2(
                m[0, 0] * v.x + m[0, 1] * v.y,
                m[1, 0] * v.x + m[1, 1] * v.y);
        }

        public override string ToString() {
            string str = "[";
            for (int i = 0; i < 2; i++) {
                str += "[";
                for (int j = 0; j < 2; j++) {
                    str += this[i, j] + ", ";
                }
                str += i < 1 ? "]," : "]";
            }
            return str + "]";
        }
    }
}