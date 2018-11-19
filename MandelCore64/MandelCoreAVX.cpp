#include "MandelCoreAVX.h"

extern "C"
{
	__declspec(dllexport) void Mandel64_AVX(float arr[], double re, double im, double step,
		int iterations, int mode, int height, int xPixel)
	{
		_mm256_zeroall();
		register __m256d reC, reZ, re2, imC, imZ, im2;
		register __m256d absZ2, iters, Bailout, One;
		__m256d yPixel;
		register int mask;
		alignas(32) double t[4], u[4];
		int pixel = 0, i, j;

		for (i = 0; i < 4; i++)
		{
			if ((mode & (1 << 30)) != 0) //Update
				while (pixel < height && arr[pixel] != 0)
					pixel++;
			t[i] = pixel++;
		}
		yPixel = _mm256_load_pd(t); //Pixel numbers that will be calculated
		absZ2 = _mm256_set1_pd(step);
		imC = _mm256_mul_pd(absZ2, yPixel); //imC = yPixel * step
		absZ2 = _mm256_set1_pd(im);
		imC = _mm256_sub_pd(absZ2, imC); //imC = im - yPixel * step
		if ((mode & (1 << 29)) == 0) //No HRAA
			reC = _mm256_set1_pd(re + step * xPixel);
		else //HRAA
		{
			for (i = 0; i < 4; i++)
				t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
			reC = _mm256_load_pd(t);
		}
		Bailout = _mm256_set1_pd(1 << 16); //Bailout = 2^16
		iters = _mm256_set1_pd(iterations); //Iters = MaxIters
		One = _mm256_set1_pd(1); //One = 1
		reZ = re2 = imZ = im2 = _mm256_setzero_pd();
		do
		{
			//Calculation: Z = Z^2 + C
			iters = _mm256_sub_pd(iters, One); //iters--
			imZ = _mm256_add_pd(imZ, imZ); //imZ = 2 * imZ
			imZ = _mm256_mul_pd(imZ, reZ); //imZ = 2 * imZ * reZ
			imZ = _mm256_add_pd(imZ, imC); //imZ = 2 * imZ * reZ + imC
			reZ = _mm256_sub_pd(re2, im2); //reZ = reZ^2 - imZ^2
			reZ = _mm256_add_pd(reZ, reC); //reZ = reZ^2 - imZ^2 + reC
			im2 = _mm256_mul_pd(imZ, imZ); //imZ^2 = imZ * imZ
			re2 = _mm256_mul_pd(reZ, reZ); //reZ^2 = reZ * reZ
										   //Check if radius or iterations exceeded
			absZ2 = _mm256_add_pd(im2, re2); //absZ^2 = imZ^2 + reZ^2
			absZ2 = _mm256_sub_pd(Bailout, absZ2); //absZ^2 = Bailout - imZ^2 - reZ^2
			absZ2 = _mm256_or_pd(absZ2, iters); //absZ^2 = absZ^2 | iters
												//Now we have '1' in a sign bit if calculation for specific variable finished
			mask = _mm256_movemask_pd(absZ2);
			if (mask) //At least one '1'
			{
				bool completed = true;
				_mm256_store_pd(t, yPixel);
				_mm256_store_pd(u, iters);
				iters = _mm256_setzero_pd();
				absZ2 = _mm256_cmp_pd(absZ2, iters, _CMP_GE_OQ);
				yPixel = _mm256_add_pd(re2, im2);
				for (i = 0; i < 4; i++)
				{
					j = (int)t[i];
					if (j >= height) continue;
					if (mask & (1 << i)) //'1' in a sign bit
					{
						if (u[i] > 0) //Reached bailout radius, not maximum iterations
							u[i] += log2(log2(yPixel.m256d_f64[i]) / 2.0);
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
				yPixel = _mm256_load_pd(t);
				iters = _mm256_load_pd(u);
				reZ = _mm256_and_pd(reZ, absZ2); //Used for conditional select
				re2 = _mm256_and_pd(re2, absZ2);
				imZ = _mm256_and_pd(imZ, absZ2);
				im2 = _mm256_and_pd(im2, absZ2);
				absZ2 = _mm256_set1_pd(step);
				imC = _mm256_mul_pd(absZ2, yPixel); //imC = yPixel * step
				absZ2 = _mm256_set1_pd(im);
				imC = _mm256_sub_pd(absZ2, imC); //imC = im - yPixel * step
				if ((mode & (1 << 29)) != 0) //HRAA
				{
					for (i = 0; i < 4; i++)
						t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
					reC = _mm256_load_pd(t);
				}
			}
		} while (true);
		_mm256_zeroall();
	}
}