interface FetcherOptions {
  method?: string;
  body?: any;
  headers?: HeadersInit;
  cache?: RequestCache;
  next?: {
    revalidate?: number | false;
    tags?: string[];
  };
}

interface IResponseModel<T> {
  success: boolean;
  message: string;
  data?: T;
}

export const fetcher = async <T>(
  url: string,
  options?: FetcherOptions
): Promise<IResponseModel<T>> => {
  try {
    const response = await fetch(process.env.BASE_URL + url, {
      method: options?.method || 'GET',
      headers: {
        'Content-Type': 'application/json',
        ...options?.headers,
      },
      body: options?.body ? JSON.stringify(options.body) : undefined,
      cache: options?.cache,
      next: options?.next,
    });

    const result = await response.json();

    if (!response.ok) {
      return {
        success: result.success || false,
        message: result.message || 'An error occurred while fetching data',
        data: undefined
      };
    }

    return {
      success: result.success || true,
      message: result.message || 'Success',
      data: result as T
    };
  } catch (error) {

    if (error instanceof Error) {
      return {
        success: false,
        message: error.message || 'An error occurred while fetching data',
        data: undefined
      };
    }
    return {
      success: false,
      message: 'An error occurred while fetching data',
      data: undefined
    };
  }
};