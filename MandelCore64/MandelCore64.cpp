#include "MandelCoreAVX.h"

extern "C"
{
	__declspec(dllexport) void Mandel64_FPU(float arr[], double re, double im, double step,
		int iterations, int mode, int height, int xPixel)
	{
		register double reZ, imZ, re2, im2, imC;
		register double Bailout = (1 << 16), reC = step * xPixel + re;
		int iterIdx;
		for (int yPixel = 0; yPixel < height; yPixel++)
		{
			if ((mode & (1 << 30)) != 0 && arr[yPixel] != 0) //Update
				continue;
			imC = im - step * yPixel;
			if ((mode & (1 << 29)) != 0) //HRAA, Re component should look like sawtooth: 0, 1, 0, 1...
				reC = re + step * ((xPixel << 1) + (yPixel & 1));
			reZ = imZ = re2 = im2 = 0;
			for (iterIdx = 0; iterIdx < iterations; iterIdx++)
			{
				imZ += imZ; //imZ = 2 * imZ
				imZ *= reZ; //imZ = 2 * imZ * reZ
				imZ += imC; //imZ = 2 * imZ * reZ + imC
				reZ = re2 - im2; //reZ = reZ^2 - imZ^2
				reZ += reC; //reZ = reZ^2 - imZ^2 + reC
				re2 = reZ * reZ; //reZ^2 = reZ * reZ
				im2 = imZ * imZ; //imZ^2 = imZ * imZ
				if (re2 + im2 > Bailout)
				{
					arr[yPixel] = (float)(iterIdx + 1 - log2(log2(re2 + im2) / 2.0));
					break;
				}
			}
			if (iterIdx == iterations)
				arr[yPixel] = (float)iterations;
		}
	}

	__declspec(dllexport) void Mandel64_SSE2(float arr[], double re, double im, double step,
		int iterations, int mode, int height, int xPixel)
	{
		register __m128d reC, reZ, re2, imC, imZ, im2;
		register __m128d absZ2, iters, Bailout, One;
		register int mask;
		__m128d yPixel, imMax = _mm_set1_pd(im);
		alignas(16) double t[2], u[2];
		int pixel = 0, i, j;

		for (i = 0; i < 2; i++)
		{
			if ((mode & (1 << 30)) != 0) //Update
				while (pixel < height && arr[pixel] != 0)
					pixel++;
			t[i] = pixel++;
		}
		yPixel = _mm_load_pd(t); //Pixel numbers that will be calculated
		absZ2 = _mm_set1_pd(step);
		imC = _mm_mul_pd(absZ2, yPixel); //imC = yPixel * step
		imC = _mm_sub_pd(imMax, imC); //imC = im - yPixel * step
		if ((mode & (1 << 29)) == 0) //No HRAA
			reC = _mm_set1_pd(re + step * xPixel);
		else //HRAA
		{
			for (i = 0; i < 2; i++)
				t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
			reC = _mm_load_pd(t);
		}
		Bailout = _mm_set1_pd(1 << 16); //Bailout = 2^16
		iters = _mm_set1_pd(iterations); //Iters = MaxIters
		One = _mm_set1_pd(1); //One = 1
		reZ = re2 = imZ = im2 = _mm_setzero_pd();
		do
		{
			//Calculation: Z = Z^2 + C
			iters = _mm_sub_pd(iters, One); //iters--
			imZ = _mm_add_pd(imZ, imZ); //imZ = 2 * imZ
			imZ = _mm_mul_pd(imZ, reZ); //imZ = 2 * imZ * reZ
			imZ = _mm_add_pd(imZ, imC); //imZ = 2 * imZ * reZ + imC
			reZ = _mm_sub_pd(re2, im2); //reZ = reZ^2 - imZ^2
			reZ = _mm_add_pd(reZ, reC); //reZ = reZ^2 - imZ^2 + reC
			im2 = _mm_mul_pd(imZ, imZ); //imZ^2 = imZ * imZ
			re2 = _mm_mul_pd(reZ, reZ); //reZ^2 = reZ * reZ
			//Checking if radius or iterations exceeded
			absZ2 = _mm_add_pd(im2, re2); //absZ^2 = imZ^2 + reZ^2
			absZ2 = _mm_sub_pd(Bailout, absZ2); //absZ^2 = Bailout - absZ2
			absZ2 = _mm_or_pd(absZ2, iters); //absZ^2 = absZ^2 | iters
			mask = _mm_movemask_pd(absZ2);
			if (mask) //At least one '1'
			{
				bool completed = true;
				_mm_store_pd(t, yPixel);
				_mm_store_pd(u, iters);
				iters = _mm_setzero_pd();
				absZ2 = _mm_cmpge_pd(absZ2, iters);
				yPixel = _mm_add_pd(re2, im2);
				for (i = 0; i < 2; i++)
				{
					j = (int)t[i];
					if (j >= height) continue;
					if (mask & (1 << i)) //'1' in a sign bit
					{
						if (u[i] > 0) //Reached bailout radius, not maximum iterations
							u[i] += log2(log2(yPixel.m128d_f64[i]) / 2.0);
						arr[j] = (float)((double)iterations - u[i]);
						if ((mode & (1 << 30)) != 0) //Update, search for the next zero
							while (pixel < height && arr[pixel] != 0)
								pixel++;
						t[i] = pixel++;
						u[i] = iterations;
					}
					completed &= (j >= height); //True only when all pixels calculated
				}
				if (completed) break; //Single exit point from the main while loop
				yPixel = _mm_load_pd(t);
				iters = _mm_load_pd(u);
				reZ = _mm_and_pd(reZ, absZ2); //Used for conditional select
				re2 = _mm_and_pd(re2, absZ2);
				imZ = _mm_and_pd(imZ, absZ2);
				im2 = _mm_and_pd(im2, absZ2);
				absZ2 = _mm_set1_pd(step);
				imC = _mm_mul_pd(absZ2, yPixel); //imC = yPixel * step
				imC = _mm_sub_pd(imMax, imC); //imC = im - yPixel * step
				if ((mode & (1 << 29)) != 0) //HRAA
				{
					for (i = 0; i < 2; i++)
						t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
					reC = _mm_load_pd(t);
				}
			}
		} while (true);
	}

	__declspec(dllexport) int GetLatestSupportedExtension()
	//No: 0; SSE2: 1; AVX: 2
	{
		int info[4], nIds;
		__cpuidex(info, 0, 0);
		nIds = info[0];
		if (nIds > 0)
		{
			__cpuidex(info, 1, 0);
			if ((info[2] & ((int)1 << 28)) != 0) //AVX
				return 2;
			if ((info[3] & ((int)1 << 26)) != 0) //SSE2
				return 1;
		}
		return 0;
	}
}